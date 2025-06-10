using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Weaving.Agents;

public static class AgentExtensions
{
    public static ChatClientBuilder UseAgents(this ChatClientBuilder builder, ChatOptions? options = default)
    {
        Throw.IfNull(builder);

        return builder.Use((inner, services) =>
        {
            var router = services.GetRequiredService<AgentRouter>();
            return new AgentRouterChatClient(router, inner);
        });
    }

    class AgentRouterChatClient(AgentRouter router, IChatClient inner) : DelegatingChatClient(inner)
    {
        public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => router.GetResponseAsync(messages, cancellationToken);

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    class AgentCoordinatorChatClient(AgentCoordinator coordinator, IChatClient inner) : DelegatingChatClient(inner)
    {
        public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => coordinator.GetResponseAsync(messages, options, cancellationToken);

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
