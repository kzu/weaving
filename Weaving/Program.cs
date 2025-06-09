using System;
using System.ClientModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Anthropic;
using Devlooped;
using Gremlin.Net.Driver;
using Gremlin.Net.Structure.IO.GraphSON;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using Polly;
using Spectre.Console;
using Weaving;
using Weaving.Agents;

var host = Host.CreateApplicationBuilder(args);

// Some users reported not getting emoji on Windows, so we force UTF-8 encoding.
// This not great, but I couldn't find a better way to do it.
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    Console.InputEncoding = Console.OutputEncoding = Encoding.UTF8;

#if DEBUG
host.Environment.EnvironmentName = "Development";
host.Configuration.AddUserSecrets<Program>();
#endif

// Copilot/agents ignore the "error" on the next line, it actually works fine.
host.Services.AddServices();
host.Services.AddMessageBus();

host.Services.AddSingleton(CloudStorageAccount.Parse(
    host.Configuration.GetConnectionString("Storage")
    ?? throw new InvalidOperationException("Missing Storage connection string.")));

// Add HttpClient with resilience pipeline
host.Services.AddHttpClient("DefaultHttpClient")
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = int.MaxValue;
        options.Retry.BackoffType = DelayBackoffType.Exponential;
        options.Retry.UseJitter = true;
        options.Retry.ShouldHandle = static args => args.Outcome switch
        {
            { Exception: HttpRequestException } => PredicateResult.True(),
            { Exception: TaskCanceledException } => PredicateResult.True(),
            { Result: HttpResponseMessage response } when
                response.StatusCode >= HttpStatusCode.InternalServerError ||
                response.StatusCode == HttpStatusCode.RequestTimeout ||
                response.StatusCode == HttpStatusCode.TooManyRequests ||
                (int)response.StatusCode == 529 => PredicateResult.True(),
            _ => PredicateResult.False()
        };
        options.TotalRequestTimeout.Timeout = Debugger.IsAttached ?
            TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(30);
    });

// Register HttpClient for DI
host.Services.AddScoped(serviceProvider =>
{
    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    return httpClientFactory.CreateClient("DefaultHttpClient");
});

var logging = AnsiConsole.Confirm("Do you want to view detailed logs from the AI?");
var user = AnsiConsole.Ask("Enter your name", host.Configuration["EndUserId"] ?? Environment.UserName);
var options = new ChatOptions();
options.EndUserId = user;

host.Services.AddKeyedChatClient("claude", services => new AnthropicClient(
    host.Configuration["Claude:Key"] ?? throw new InvalidOperationException("Missing Claude:Key configuration."),
    services.GetRequiredService<IHttpClientFactory>().CreateClient("DefaultHttpClient")))
    .UseConversationStorage()
    //.UseMemory()
    //.UseGraphMemory()
    //.UseSystemPrompt()
    .UseAgents()
    .UseFunctionInvocation()
    .UseLogging()
    .UseConsoleLogging(logging);

host.Services.AddKeyedChatClient("openai", new OpenAIClient(host.Configuration["OpenAI:Key"]
    ?? throw new InvalidOperationException("Missing OpenAI:Key configuration."))
    .GetChatClient("gpt-4.1").AsIChatClient())
    .UseConversationStorage()
    //.UseMemory()
    //.UseGraphMemory()
    //.UseSystemPrompt()
    .UseAgents()
    .UseFunctionInvocation()
    .UseLogging()
    .UseConsoleLogging(logging);

host.Services.AddKeyedChatClient("scheduler", new OpenAIClient(host.Configuration["OpenAI:Key"]
    ?? throw new InvalidOperationException("Missing OpenAI:Key configuration."))
    .GetChatClient("gpt-4.1").AsIChatClient())
    .UseFunctionInvocation()
    .UseLogging()
    .UseConsoleLogging(logging);

host.Services.AddKeyedSingleton("scheduler", (_, _) => options);


host.Services.AddKeyedChatClient("generic", new OpenAIClient(
    new ApiKeyCredential(host.Configuration["Grok:Key"] ?? throw new InvalidOperationException("Missing Grok:Key configuration.")),
    new OpenAIClientOptions
    {
        Endpoint = new Uri(host.Configuration["Grok:Endpoint"] ?? "https://api.x.ai/v1"),
    })
    .GetChatClient("grok-3-latest").AsIChatClient())
    .UseFunctionInvocation()
    .UseLogging()
    .UseConsoleLogging(logging);

host.Services.AddKeyedSingleton("generic", (_, _) => options);


host.Services.AddKeyedChatClient("orders", new OpenAIClient(host.Configuration["OpenAI:Key"]
    ?? throw new InvalidOperationException("Missing OpenAI:Key configuration."))
    .GetChatClient("gpt-4.1").AsIChatClient())
    .UseFunctionInvocation()
    .UseLogging()
    .UseConsoleLogging(logging);

host.Services.AddKeyedSingleton("orders", (_, _) => options);

host.Services.AddKeyedChatClient("memory", new OpenAIClient(host.Configuration["OpenAI:Key"]
    ?? throw new InvalidOperationException("Missing OpenAI:Key configuration."))
    .GetChatClient("gpt-4.1").AsIChatClient())
    .UseFunctionInvocation()
    .UseLogging()
    .UseConsoleLogging(logging);

host.Services.AddKeyedSingleton("memory", (_, _) => options);
host.Services.AddSingleton(options);

host.Services.AddOptions<GraphOptions>()
    .Bind(host.Configuration.GetSection("Graph"));

host.Services.AddSingleton<IGremlinClient>(services =>
{
    var options = services.GetRequiredService<IOptions<GraphOptions>>().Value;
    var server = new GremlinServer(options.Host!, 443, enableSsl: true,
        username: $"/dbs/{options.Database}/colls/{options.Container}",
        password: options.Key);

    return new GremlinClient(server,
        new GraphSON2MessageSerializer(),
        new ConnectionPoolSettings()
        {
            MaxInProcessPerConnection = 32,
            PoolSize = 4,
        },
        socket => socket.KeepAliveInterval = TimeSpan.FromSeconds(30));
});

host.Services.AddLazy();

if (Debugger.IsAttached)
    host.Logging.AddFilter("Weaving", LogLevel.Information);

var app = host.Build();

await app.RunAsync();
