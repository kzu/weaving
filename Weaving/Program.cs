using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Anthropic;
using Devlooped;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenAI;
using Polly;
using Spectre.Console;
using Weaving;

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

var builder = host.Services.AddKeyedChatClient("claude", services => new AnthropicClient(
    host.Configuration["Claude:Key"] ?? throw new InvalidOperationException("Missing Claude:Key configuration."),
    services.GetRequiredService<IHttpClientFactory>().CreateClient("DefaultHttpClient")))
    .UseConversationStorage()
    //.UseMemory()
    .UseGraphMemory()
    .UseSystemPrompt()
    .UseFunctionInvocation();

if (logging)
    builder.UseLogging().UseConsoleLogging();

builder = host.Services.AddKeyedChatClient("openai", new OpenAIClient(host.Configuration["OpenAI:Key"]
    ?? throw new InvalidOperationException("Missing OpenAI:Key configuration."))
    .GetChatClient("gpt-4.1").AsIChatClient())
    .UseConversationStorage()
    //.UseMemory()
    .UseGraphMemory()
    .UseSystemPrompt()
    .UseFunctionInvocation();

if (logging)
    builder.UseLogging().UseConsoleLogging();

host.Services.AddSingleton(services =>
{
    var agents = services.GetServices<IAgent>();
    var options = new ChatOptions
    {
        Tools =
        [
           AIFunctionFactory.Create(() => DateTimeOffset.Now, "get_date", "Gets the current date time (with offset)."),
        ]
    };
    foreach (var agent in agents)
    {
        // Configures individual agent execution
        options.Tools.Add(AIFunctionFactory.Create(agent.Execute, agent.Id, agent.Capabilities));
    }
    options.AddSystemPrompt(Constants.SystemPrompt);
    return options;
});

host.Services.AddLazy();

if (Debugger.IsAttached)
    host.Logging.AddFilter("Weaving", LogLevel.Information);

var app = host.Build();

await app.RunAsync();
