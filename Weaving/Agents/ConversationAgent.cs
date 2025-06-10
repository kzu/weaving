using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Weaving.Agents;

[Service]
public class ConversationAgent(IServiceProvider services, Lazy<IEnumerable<IAgent>> agents) : ConfigurableAgent(services, "agents:conversation")
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellation = default)
    {
        var system = JsonSerializer.Serialize(agents.Value, JsonOptions.Default);
        return await Client.GetResponseAsync(
            [new ChatMessage(ChatRole.System, Prompt!.Replace("{agents", system)), .. messages],
            Options, cancellation);
    }
}
