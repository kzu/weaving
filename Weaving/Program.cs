using System;
using System.Diagnostics;
using System.IO;
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
using Polly;
using Spectre.Console;
using Tomlyn.Extensions.Configuration;
using Weaving;
using Weaving.Agents;

var host = Host.CreateApplicationBuilder(args);

// Some users reported not getting emoji on Windows, so we force UTF-8 encoding.
// This not great, but I couldn't find a better way to do it.
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    Console.InputEncoding = Console.OutputEncoding = Encoding.UTF8;

foreach (var toml in Directory.EnumerateFiles(".", "*.toml", SearchOption.AllDirectories))
{
    host.Configuration.AddTomlFile(toml, optional: false, reloadOnChange: false);
}

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
host.Services.AddHttpClient("claude")
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

var logging = AnsiConsole.Confirm("Do you want to view detailed logs from the AI?");
var user = AnsiConsole.Ask("Enter your name", host.Configuration["EndUserId"] ?? Environment.UserName);

host.Services.AddKeyedChatClient("claude", services => new AnthropicClient(
    host.Configuration["Claude:Key"] ?? throw new InvalidOperationException("Missing Claude:Key configuration."),
    services.GetRequiredService<IHttpClientFactory>().CreateClient("claude")))
    .UseAgents()
    .UseStandardChat(user, logging);

// Configures all clients in openai.toml
host.Services.UseOpenAI(host.Configuration, user, logging, (id, builder) =>
{
    if (id == "openai")
        builder.UseAgents();
});

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
