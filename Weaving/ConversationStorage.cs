using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Devlooped;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Weaving;

public class ConversationChatClient(IChatClient innerClient, CloudStorageAccount storage, ILogger<ConversationChatClient> logger) : DelegatingChatClient(innerClient)
{
    IDocumentPartition<ChatConversation> repo = DocumentPartition.Create<ChatConversation>(
        storage, "Weaving", "Conversations", x => x.Id);

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        ChatConversation conversation;

        if (options?.ConversationId is not { } conversationId)
        {
            conversationId = Ulid.NewUlid().ToString();
            (options ??= new()).ConversationId = conversationId;
            logger.LogInformation("Creating new conversation with ID {ConversationId}.", conversationId);
            conversation = new ChatConversation(conversationId, [.. messages]);
        }
        else
        {
            if (await repo.GetAsync(conversationId, cancellationToken) is { } existing)
            {
                logger.LogInformation("Found existing conversation with ID {ConversationId}.", conversationId);
                conversation = existing;
                conversation.Messages.AddRange(messages);
            }
            else
            {
                logger.LogInformation("Creating new conversation with ID {ConversationId}.", conversationId);
                conversation = new ChatConversation(conversationId, [.. messages]);
            }
        }

        var response = await base.GetResponseAsync(conversation.Messages, options, cancellationToken);
        conversation.Messages.AddRange(response.Messages);
        response.ConversationId = conversation.Id;

        await repo.PutAsync(conversation, cancellationToken);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatConversation conversation;

        if (options?.ConversationId is not { } conversationId)
        {
            conversationId = Ulid.NewUlid().ToString();
            (options ??= new()).ConversationId = conversationId;
            conversation = new ChatConversation(conversationId, [.. messages]);
        }
        else
        {
            if (await repo.GetAsync(conversationId, cancellationToken) is { } existing)
            {
                conversation = existing;
                conversation.Messages.AddRange(messages);
            }
            else
            {
                conversation = new ChatConversation(conversationId, [.. messages]);
            }
        }

        List<ChatResponseUpdate> updates = [];

        await foreach (var update in base.GetStreamingResponseAsync(conversation.Messages, options, cancellationToken))
        {
            updates.Add(update);
            update.ConversationId = conversationId;
            yield return update;
        }

        var response = updates.ToChatResponse();
        conversation.Messages.AddRange(response.Messages);
        await repo.PutAsync(conversation, cancellationToken);
    }

    record ChatConversation(string Id, List<ChatMessage> Messages);
}

public static class ConversationStorage
{
    public static ChatClientBuilder UseConversationStorage(
        this ChatClientBuilder builder,
        CloudStorageAccount? storage = null,
        ILoggerFactory? loggerFactory = null,
        Action<ConversationChatClient>? configure = null)
    {
        _ = Throw.IfNull(builder);

        return builder.Use((innerClient, services) =>
        {
            storage ??= services.GetRequiredService<CloudStorageAccount>();
            loggerFactory ??= services.GetService<ILoggerFactory>();

            var chatClient = new ConversationChatClient(innerClient, storage, loggerFactory.CreateLogger<ConversationChatClient>());
            configure?.Invoke(chatClient);
            return chatClient;
        });
    }
}
