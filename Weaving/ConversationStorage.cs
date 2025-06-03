using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Devlooped;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Weaving;

public class ConversationChatClient(IChatClient innerClient, CloudStorageAccount storage, ILogger<ConversationChatClient> logger) : DelegatingChatClient(innerClient)
{
    const string SystemPrompt =
        """
        # Conversation Storage
        1. ALWAYS track EVERY topic on EVERY conversation
        2. Save the conversation identifier and topic(s) using the conversation_ functions. 
           These are separate from the general purpose knowledge graph memory and are used 
           specifically for conversations and their identifiers. 
        3. When user asks about past conversations, use the conversation functions to find by topic and 
           read the conversation history as needed using the returned conversation identifiers and the 
           conversation_read_history function.
        4. Always ensure that conversation identifiers are handled securely and never exposed to users.
        """;

    IDocumentPartition<ChatConversation> history = DocumentPartition.Create<ChatConversation>(
        storage, "Weaving", "Conversations", x => x.Id);

    ITableRepository<TableEntity> topics = TableRepository.Create(storage, "Topics");

    bool useConversationTopics = false;

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        ChatConversation conversation;
        if (useConversationTopics)
            (options ??= new ChatOptions()).AddSystemPrompt(SystemPrompt);

        if (options?.ConversationId is not { } conversationId)
        {
            conversationId = Ulid.NewUlid().ToString();
            (options ??= new ChatOptions()).ConversationId = conversationId;
            logger.LogInformation("Creating new conversation with ID {ConversationId}.", conversationId);
            conversation = new ChatConversation(conversationId, [
                new ChatMessage(ChatRole.System, $"Conversation ID is {conversationId}"),
                .. messages]);
        }
        else
        {
            if (await history.GetAsync(conversationId, cancellationToken) is { } existing)
            {
                logger.LogInformation("Found existing conversation with ID {ConversationId}.", conversationId);
                conversation = existing;
                conversation.Messages.AddRange(messages);
            }
            else
            {
                logger.LogInformation("Creating new conversation with ID {ConversationId}.", conversationId);
                conversation = new ChatConversation(conversationId, [
                    new ChatMessage(ChatRole.System, $"Conversation ID is {conversationId}"),
                .. messages]);
            }
        }

        var response = await base.GetResponseAsync(conversation.Messages, SetupTools(options), cancellationToken);
        conversation.Messages.AddRange(response.Messages);
        response.ConversationId = conversation.Id;

        await history.PutAsync(conversation, cancellationToken);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatConversation conversation;
        if (useConversationTopics)
            (options ??= new ChatOptions()).AddSystemPrompt(SystemPrompt);

        if (options?.ConversationId is not { } conversationId)
        {
            conversationId = Ulid.NewUlid().ToString();
            (options ??= new ChatOptions()).ConversationId = conversationId;
            conversation = new ChatConversation(conversationId, [.. messages]);
        }
        else
        {
            if (await history.GetAsync(conversationId, cancellationToken) is { } existing)
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

        await foreach (var update in base.GetStreamingResponseAsync(conversation.Messages, SetupTools(options), cancellationToken))
        {
            updates.Add(update);
            update.ConversationId = conversationId;
            yield return update;
        }

        var response = updates.ToChatResponse();
        conversation.Messages.AddRange(response.Messages);
        await history.PutAsync(conversation, cancellationToken);
    }

    record ChatConversation(string Id, List<ChatMessage> Messages);

    ChatOptions? SetupTools(ChatOptions? options)
    {
        if (!useConversationTopics)
            return options;

        options ??= new ChatOptions();
        options.Tools ??= [];
        if (!options.Tools.Any(x => x.Name == "conversation_read_history"))
        {
            options.Tools.Add(AIFunctionFactory.Create(ReadHistory, "conversation_read_history"));
            options.Tools.Add(AIFunctionFactory.Create(AddTopics, "conversation_add_topics"));
            options.Tools.Add(AIFunctionFactory.Create(ReadTopics, "conversation_read_topic"));
            options.Tools.Add(AIFunctionFactory.Create(FindConversations, "conversation_find_by_topics"));
        }

        return options;
    }

    [Description("Reads the existing chat messages that ocurred in the given conversation.")]
    async Task<IEnumerable<ChatMessage>> ReadHistory(string conversationId)
    {
        if (await history.GetAsync(conversationId) is { } conversation)
            return conversation.Messages;

        return [];
    }

    [Description("Adds topics to the given conversation")]
    async Task AddTopics(string conversationId, string[] topics)
    {
        if (topics is null || topics.Length == 0)
            return;

        await this.topics.PutAsync(topics.Select(x => new TableEntity(conversationId,
            TableStorageAttribute.Sanitize(x.ToUpperInvariant()))));
    }

    [Description("Reads the topics associated with the given conversation.")]
    async IAsyncEnumerable<string> ReadTopics(string conversationId)
    {
        await foreach (var topic in topics.EnumerateAsync(conversationId))
        {
            yield return topic.RowKey;
        }
    }

    [Description("Finds conversation identifiers that match the given topics (case-insensitive). The full conversations can then be read with those identifiers using the conversation_read_history.")]
    async Task<string[]> FindConversations(string[] topics)
    {
        var table = storage.CreateTableServiceClient().GetTableClient("Topics");
        var conversations = new HashSet<string>();
        // Query all entities once, then filter in memory for each topic (case-insensitive)
        if (topics is null || topics.Length == 0)
            return [];

        topics = topics.Select(x => TableStorageAttribute.Sanitize(x.ToUpperInvariant())).ToArray();
        await foreach (var entity in this.topics.EnumerateAsync())
        {
            foreach (var topic in topics)
            {
                if (entity.RowKey.ToUpperInvariant().Contains(topic))
                    conversations.Add(entity.PartitionKey);
            }
        }

        return conversations.ToArray();
    }
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
            loggerFactory ??= services.GetRequiredService<ILoggerFactory>();

            var chatClient = new ConversationChatClient(innerClient, storage, loggerFactory.CreateLogger<ConversationChatClient>());
            configure?.Invoke(chatClient);
            return chatClient;
        });
    }
}
