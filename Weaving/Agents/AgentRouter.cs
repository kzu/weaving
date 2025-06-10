using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Weaving.Agents;

public record AgentRouterOptions(string Client, string Prompt, ChatOptions Options);

[Service]
public class AgentRouter
{
    readonly AgentRouterOptions options;
    readonly string agentsJson;
    readonly IDictionary<string, IAgent> agentsMap;
    readonly IChatClient chatClient;

    public AgentRouter(IServiceProvider services, IConfiguration configuration, IEnumerable<IAgent> agents, ILogger<AgentRouter> logger)
    {
        options = configuration.GetRequiredSection("agents:router").Get<AgentRouterOptions>()
            ?? throw new InvalidOperationException("AgentRouter options not configured.");

        agentsJson = JsonSerializer.Serialize(agents, JsonOptions.Default);
        agentsMap = agents.ToDictionary(a => a.Name);
        chatClient = services.GetRequiredKeyedService<IChatClient>(options.Client);
    }

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellation = default)
    {
        var messagesJson = JsonSerializer.Serialize(messages, JsonOptions.Default);
        var systemPrompt = options.Prompt
            .Replace("{{agents}}", agentsJson)
            .Replace("{{messages}}", messagesJson);

        var agentName = await chatClient.GetResponseAsync<AgentResponse>(
            [new ChatMessage(ChatRole.System, systemPrompt)],
            options.Options, true, cancellation);

        if (agentsMap.TryGetValue(agentName.Result.Agent, out var agent))
        {
            return await agent.GetResponseAsync(messages, cancellation);
        }

        return new ChatResponse();
    }

    record AgentResponse(string Agent);
}
