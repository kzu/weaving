using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Gremlin.Net.Driver;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Weaving.Agents;

//[Service]
public class MemoryUpdatingAgent : GremlinAgent, IAgent
{
    readonly IChatClient chat;
    readonly ChatOptions options;

    public MemoryUpdatingAgent(
        [FromKeyedServices("memory")] IChatClient chat,
        [FromKeyedServices("memory")] ChatOptions options,
        IGremlinClient graph,
        ILogger<MemoryUpdatingAgent> logger) : base(graph, logger)
    {
        this.chat = chat;
        this.options = options.Clone();
        this.options.Tools ??= [];
        this.options.Tools.Add(AIFunctionFactory.Create(Update, "memory_update"));
    }

    public string Name => "memory_update";

    public string Capabilities =>
        """
        # Knowledge Graph Memory Persistence:
        
        1. Memory Areas
           - While conversing with the user, be attentive to any new information that falls into these categories:
             a) Basic Identity (age, gender, location, job title, education level, etc.)
             b) Behaviors (interests, habits, etc.)
             c) Preferences (communication style, preferred language, etc.)
             d) Goals (goals, targets, aspirations, etc.)
             e) Relationships (personal and professional relationships up to 3 degrees of separation)
        
        2. Memory Update:
           - If any new information was gathered during the interaction, update your memory accordingly.
           - Leverage known ontologies for defining entities, relationships and values. In particular: 
             a) Use FOAF (Friend of a Friend) for people and relationships
             b) Use schema.org for general entities and relationships, in particular: 
                * Person https://schema.org/Person
                * Organization https://schema.org/Organization
                * LocalBusiness https://schema.org/LocalBusiness
                * Product https://schema.org/Product
                * Event https://schema.org/Event
                * Article https://schema.org/Article
                * Recipe https://schema.org/Recipe
                * Review https://schema.org/Review
           - In addition to the structured data above:
             a) Create entities for recurring organizations, people, and significant events
             b) Connect them to the current entities using relations
             b) Store facts about them as observations        
        """;

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellation = default)
    {
        var messagesJson = JsonSerializer.Serialize(messages, JsonOptions.Default);
        var response = await chat.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, Capabilities),
                new ChatMessage(ChatRole.User,
                $"""
                Given the following user conversation: 
                {messagesJson}

                Actively try to identify any new information that can be added to the knowledge graph memory.
                """)
            ], options, cancellation);

        return new ChatResponse();
    }

    [Description(
        $"""
        Updates relevant memories that can provide useful context in future user conversations.
        {GremlinDescription}
        """)]
    async Task<ResultSet<object>> Update(
        [Description(
            """
            Gremlin API (add/update) script to be run against Azure Cosmos DB Graph API.
            NOTE: multi-statement groovy scripts are not supported.
            The script MUST always filter vertices and edges by a property named `tenantId` with a value 
            of `$$partitionKey$$` which is used as a variable substitution reference.
            """)] string script)
    {
        if (FunctionInvokingChatClient.CurrentContext?.Options?.EndUserId is not string userId)
            return new ResultSet<object>([], new Dictionary<string, object>());

        var result = await ExecuteQuery(script, userId);

        // We always terminate after saving.
        //FunctionInvokingChatClient.CurrentContext?.Terminate = true;

        return result;
    }
}
