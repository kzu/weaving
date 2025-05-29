using System;
using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;
using Weaving;

var host = Host.CreateApplicationBuilder(args);

#if DEBUG
host.Environment.EnvironmentName = "Development";
host.Configuration.AddUserSecrets<Program>();
#endif

host.Services.AddServices();
host.Services.AddMessageBus();

host.Services.AddChatClient(new AnthropicClient(host.Configuration["Claude:Key"]
    ?? throw new InvalidOperationException("Missing Claude:Key configuration.")))
    .UseFunctionInvocation();

host.Services.AddKeyedChatClient("openai", new OpenAIClient(host.Configuration["OpenAI:Key"]
    ?? throw new InvalidOperationException("Missing OpenAI:Key configuration."))
    .GetChatClient("gpt-4o").AsIChatClient())
    .UseFunctionInvocation();

host.Services.AddSingleton(services =>
{
    var agents = services.GetServices<IAgent>();
    var options = new ChatOptions
    {
        Tools = []
    };
    foreach (var agent in agents)
    {
        // Configures individual agent execution
        options.Tools.Add(AIFunctionFactory.Create(agent.Execute, agent.Id, agent.Capabilities));
    }
    options.SystemPrompt = Constants.SystemPrompt;
    return options;
});

host.Services.AddLazy();

var app = host.Build();

await app.RunAsync();
