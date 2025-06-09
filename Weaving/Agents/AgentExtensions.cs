using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Weaving.Agents;

public static class AgentExtensions
{
    public static ChatClientBuilder UseAgents(this ChatClientBuilder builder, ChatOptions? options = default)
    {
        Throw.IfNull(builder);

        return builder.Use((inner, services) =>
        {
            options ??= services.GetService<ChatOptions>();
            var agents = services.GetServices<IAgent>();
            var logger = services.GetRequiredService<ILogger<AgentCoordinator>>();
            return new AgentCoordinatorChatClient(new AgentCoordinator(inner, agents, logger), inner);
        });
    }

    class AgentCoordinatorChatClient(AgentCoordinator coordinator, IChatClient inner) : DelegatingChatClient(inner)
    {
        public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            return coordinator.GetResponseAsync(messages, options, cancellationToken);
        }

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
