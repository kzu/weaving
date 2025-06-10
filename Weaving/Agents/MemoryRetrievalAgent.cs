using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Gremlin.Net.Driver;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Weaving.Agents;

//[Service]
public class MemoryRetrievalAgent : GremlinAgent, IAgent
{
    readonly IChatClient chat;
    readonly ChatOptions options;

    public MemoryRetrievalAgent(
        [FromKeyedServices("memory")] IChatClient chat,
        [FromKeyedServices("memory")] ChatOptions options,
        IGremlinClient graph,
        ILogger<MemoryRetrievalAgent> logger) : base(graph, logger)
    {
        this.chat = chat;
        this.options = options.Clone();
        this.options.Tools ??= [];
        this.options.Tools.Add(AIFunctionFactory.Create(Query, "memory_recall"));
    }

    public string Name => "memory_retrieval";

    public string Description =>
        """
        # Knowledge Graph Memory Retrieval:
        
        1. Retrieval:
           - Always refer to your knowledge graph as your "memory"
           - Always retrieve user preferences related to the current conversation
        
        2. Memory
           - Can provide access to user information that falls into these categories:
             a) Basic Identity (age, gender, location, job title, education level, etc.)
             b) Behaviors (interests, habits, etc.)
             c) Preferences (communication style, preferred language, etc.)
             d) Goals (goals, targets, aspirations, etc.)
             e) Relationships (personal and professional relationships up to 3 degrees of separation)
        
        3. Ontologies
           - The memory graph leverages known ontologies for defining entities, relationships and values. In particular: 
             a) Uses FOAF (Friend of a Friend) for people and relationships
             b) Uses schema.org for general entities and relationships, in particular: 
                * Person https://schema.org/Person
                * Organization https://schema.org/Organization
                * LocalBusiness https://schema.org/LocalBusiness
                * Product https://schema.org/Product
                * Event https://schema.org/Event
                * Article https://schema.org/Article
                * Recipe https://schema.org/Recipe
                * Review https://schema.org/Review        

        4. In addition to the structured data ontologies above, the graph keeps track of:
             a) Create entities for recurring organizations, people, and significant events
             b) Connect them to the current entities using relations
             b) Store facts about them as observations        

        Never refer to the implementation details of graph databases, graphs, vertices, edges, properties, etc.
        They should all be referred to as "memory" or "knowledge".
        """;

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellation = default)
    {
        var messagesJson = JsonSerializer.Serialize(messages, JsonOptions.Default);
        var response = await chat.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, Description),
                new ChatMessage(ChatRole.User,
                $"""
                Given the following user conversation: 
                {messagesJson}

                Retrieve relevant memories that can provide useful context to continue the conversation.
                """)
            ], options, cancellation);

        if (response.FinishReason == ChatFinishReason.ToolCalls &&
            response.Messages.SelectMany(x => x.Contents).OfType<FunctionResultContent>().LastOrDefault() is { } result &&
            result.Result is JsonElement json &&
            json.ValueKind == JsonValueKind.Array &&
            json.EnumerateArray().Any())
        {
            return new ChatResponse(new ChatMessage(ChatRole.System,
                $"""
                The following memories were retrieved from the knowledge graph memory:
                {JsonSerializer.Serialize(json, JsonOptions.Default)}
                """));
        }

        return new ChatResponse();
    }

    [Description(
        $"""
        Retrieves relevant memories that can provide useful context in user conversations.
        {GremlinDescription}
        """)]
    async Task<ResultSet<object>> Query(
        [Description(
            """
            Gremlin API (read/query only) Script that never mutates the graph. 
            NOTE: multi-statement groovy scripts are not supported.            
            """)] string query)
    {
        if (FunctionInvokingChatClient.CurrentContext?.Options?.EndUserId is not string userId)
            return new ResultSet<object>([], new Dictionary<string, object>());

        var result = await ExecuteQuery(query, userId);
        if (result.Count == 0)
        {
            // Stop further processing if no results are found
            FunctionInvokingChatClient.CurrentContext?.Terminate = true;
        }

        return result;
    }
}
