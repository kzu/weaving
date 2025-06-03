using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Weaving;

public static partial class MemoryExtensions
{
    const string SystemPrompt =
        """
        # Knowledge Graph Memory Management:
        
        Follow these steps for each interaction:
        
        1. User Identification:
           - You should assume that you are interacting with default_user
        
        2. Memory Retrieval:
           - Always begin your chat by saying only "Remembering..." and retrieve all relevant information from your knowledge graph
           - Always refer to your knowledge graph as your "memory"
           - Always retrieve user preferences before making any kind of recommendations about any topic
        
        3. Memory
           - While conversing with the user, be attentive to any new information that falls into these categories:
             a) Basic Identity (age, gender, location, job title, education level, etc.)
             b) Behaviors (interests, habits, etc.)
             c) Preferences (communication style, preferred language, etc.)
             d) Goals (goals, targets, aspirations, etc.)
             e) Relationships (personal and professional relationships up to 3 degrees of separation)
        
        4. Memory Update:
           - If any new information was gathered during the interaction, update your memory as follows:
             a) Create entities for recurring organizations, people, and significant events
             b) Connect them to the current entities using relations
             b) Store facts about them as observations

        """;

    extension(ChatOptions options)
    {
        public string? EndUserId
        {
            get => (options.AdditionalProperties ??= []).TryGetValue("EndUserId", out var value) ? value as string : null;
            set => (options.AdditionalProperties ??= [])["EndUserId"] = value;
        }
    }

    public static ChatClientBuilder UseMemory(this ChatClientBuilder builder)
    {
        _ = Throw.IfNull(builder);

        return builder.Use((inner, services) => new MemoryChatClient(inner));
    }

    partial class MemoryChatClient(IChatClient inner) : DelegatingChatClient(inner)
    {
        static readonly AsyncLocal<string?> currentUserId = new();
        string? CurrentUserId => currentUserId.Value;

        public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            currentUserId.Value = options?.EndUserId;

            SetupTools(options);

            return base.GetResponseAsync(messages, options, cancellationToken);
        }

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            currentUserId.Value = options?.EndUserId;

            SetupTools(options);

            return base.GetStreamingResponseAsync(messages, options, cancellationToken);
        }

        void SetupTools(ChatOptions? options)
        {
            if (options?.EndUserId is { Length: > 0 } userId)
            {
                options.AddSystemPrompt(SystemPrompt);
                options.Tools ??= [];
                var tools = options.Tools;
                if (!options.Tools.Any(x => x.Name == "memory_read_graph"))
                {
                    tools.Add(AIFunctionFactory.Create(ReadGraph, "memory_read_graph"));
                    tools.Add(AIFunctionFactory.Create(SearchNodes, "memory_search_nodes"));
                    tools.Add(AIFunctionFactory.Create(CreateEntities, "memory_create_entities"));
                    tools.Add(AIFunctionFactory.Create(CreateRelations, "memory_create_relations"));
                    tools.Add(AIFunctionFactory.Create(AddObservations, "memory_add_observations"));
                    tools.Add(AIFunctionFactory.Create(DeleteEntities, "memory_delete_entities"));
                    tools.Add(AIFunctionFactory.Create(DeleteObservations, "memory_delete_observations"));
                    tools.Add(AIFunctionFactory.Create(DeleteRelations, "memory_delete_relations"));
                    tools.Add(AIFunctionFactory.Create(OpenNodes, "memory_open_nodes"));
                }
            }
        }

        KnowledgeGraph LoadGraph(string userId)
        {
            string path = GetMemoryPath(userId);
            if (!File.Exists(path))
                return new KnowledgeGraph([], []);

            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<KnowledgeGraph>(stream, JsonContext.DefaultOptions)
                ?? new KnowledgeGraph([], []);
        }

        void SaveGraph(string userId, KnowledgeGraph graph)
        {
            string path = GetMemoryPath(userId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = File.Create(path);
            JsonSerializer.Serialize(stream, graph, JsonContext.DefaultOptions);
        }

        [Description("Returns the entire knowledge graph for the current user, including all entities and relations.")]
        KnowledgeGraph ReadGraph()
        {
            if (CurrentUserId is null)
                return new KnowledgeGraph([], []);
            var graph = LoadGraph(CurrentUserId);
            return graph;
        }

        [Description("Searches for nodes (entities) in the knowledge graph whose names match the given query string.")]
        KnowledgeGraph SearchNodes(string query)
        {
            if (CurrentUserId is null)
                return new KnowledgeGraph([], []);
            var graph = LoadGraph(CurrentUserId);
            var entities = graph.Entities
                .Where(e => e.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var entityNames = entities.Select(e => e.Name).ToHashSet();
            var relations = graph.Relations
                .Where(r => entityNames.Contains(r.From) || entityNames.Contains(r.To))
                .ToArray();
            return new KnowledgeGraph(entities, relations);
        }

        [Description("Adds new entities to the knowledge graph. If an entity with the same name already exists, it will be updated.")]
        void CreateEntities(Entity[] entities)
        {
            if (CurrentUserId is null)
                return;
            var graph = LoadGraph(CurrentUserId);
            var entityDict = graph.Entities.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var entity in entities)
            {
                if (entityDict.TryGetValue(entity.Name, out var existing))
                {
                    // Merge observations (union)
                    var mergedObs = existing.Observations.Union(entity.Observations).Distinct().ToArray();
                    entityDict[entity.Name] = entity with { Observations = mergedObs };
                }
                else
                {
                    entityDict[entity.Name] = entity;
                }
            }
            SaveGraph(CurrentUserId, new KnowledgeGraph(entityDict.Values.ToArray(), graph.Relations));
        }

        [Description("Adds new relations between entities in the knowledge graph. If a relation already exists, it will be updated.")]
        void CreateRelations(Relation[] relations)
        {
            if (CurrentUserId is null)
                return;
            var graph = LoadGraph(CurrentUserId);
            var relSet = new HashSet<(string, string, string)>(graph.Relations.Select(r => (r.From, r.To, r.Type)));
            var newRelations = graph.Relations.ToList();
            foreach (var rel in relations)
            {
                var key = (rel.From, rel.To, rel.Type);
                if (!relSet.Contains(key))
                {
                    newRelations.Add(rel);
                    relSet.Add(key);
                }
                // else: already exists, do nothing (update semantics could be added if needed)
            }
            SaveGraph(CurrentUserId, new KnowledgeGraph(graph.Entities, newRelations.ToArray()));
        }

        [Description("Adds new observations to existing entities in the knowledge graph.")]
        void AddObservations(Observation[] observations)
        {
            if (CurrentUserId is null)
                return;
            var graph = LoadGraph(CurrentUserId);
            var entityDict = graph.Entities.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var obs in observations)
            {
                if (entityDict.TryGetValue(obs.Entity, out var entity))
                {
                    var mergedObs = entity.Observations.Union(obs.Contents).Distinct().ToArray();
                    entityDict[obs.Entity] = entity with { Observations = mergedObs };
                }
            }
            SaveGraph(CurrentUserId, new KnowledgeGraph(entityDict.Values.ToArray(), graph.Relations));
        }

        [Description("Deletes entities from the knowledge graph by name. All relations and observations associated with the entity will also be deleted.")]
        void DeleteEntities(string[] entities)
        {
            if (CurrentUserId is null)
                return;
            var graph = LoadGraph(CurrentUserId);
            var toDelete = new HashSet<string>(entities, StringComparer.OrdinalIgnoreCase);
            var newEntities = graph.Entities.Where(e => !toDelete.Contains(e.Name)).ToArray();
            var newRelations = graph.Relations.Where(r => !toDelete.Contains(r.From) && !toDelete.Contains(r.To)).ToArray();
            SaveGraph(CurrentUserId, new KnowledgeGraph(newEntities, newRelations));
        }

        [Description("Deletes specific observations from entities in the knowledge graph.")]
        void DeleteObservations(Observation[] observations)
        {
            if (CurrentUserId is null)
                return;
            var graph = LoadGraph(CurrentUserId);
            var entityDict = graph.Entities.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var obs in observations)
            {
                if (entityDict.TryGetValue(obs.Entity, out var entity))
                {
                    var newObs = entity.Observations.Except(obs.Contents).ToArray();
                    entityDict[obs.Entity] = entity with { Observations = newObs };
                }
            }
            SaveGraph(CurrentUserId, new KnowledgeGraph(entityDict.Values.ToArray(), graph.Relations));
        }

        [Description("Deletes relations between entities in the knowledge graph.")]
        void DeleteRelations(Relation[] relations)
        {
            if (CurrentUserId is null)
                return;
            var graph = LoadGraph(CurrentUserId);
            var toDelete = new HashSet<(string, string, string)>(relations.Select(r => (r.From, r.To, r.Type)));
            var newRelations = graph.Relations.Where(r => !toDelete.Contains((r.From, r.To, r.Type))).ToArray();
            SaveGraph(CurrentUserId, new KnowledgeGraph(graph.Entities, newRelations));
        }

        [Description("Returns the subgraph containing the specified nodes and their direct relations.")]
        KnowledgeGraph OpenNodes(string[] names)
        {
            if (CurrentUserId is null)
                return new KnowledgeGraph([], []);
            var graph = LoadGraph(CurrentUserId);
            var nameSet = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            var entities = graph.Entities.Where(e => nameSet.Contains(e.Name)).ToArray();
            var relations = graph.Relations.Where(r => nameSet.Contains(r.From) || nameSet.Contains(r.To)).ToArray();
            var relatedNames = new HashSet<string>(entities.Select(e => e.Name));
            foreach (var r in relations)
            {
                relatedNames.Add(r.From);
                relatedNames.Add(r.To);
            }
            var allEntities = graph.Entities.Where(e => relatedNames.Contains(e.Name)).ToArray();
            return new KnowledgeGraph(allEntities, relations);
        }

        static string GetMemoryPath(string userId)
        {
            // We want a location where permissions are expected to be restricted to the current user.
            string directory = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Path.GetTempPath()
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // Include entry point file name so the directory name is not completely opaque.
            return Path.Join(directory, "dotnet", "mcp", "memory", userId + ".json");
        }
    }

    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
        WriteIndented = true
        )]
    [JsonSerializable(typeof(Entity))]
    [JsonSerializable(typeof(Relation))]
    [JsonSerializable(typeof(Observation))]
    [JsonSerializable(typeof(KnowledgeGraph))]
    partial class JsonContext : JsonSerializerContext
    {
        static readonly Lazy<JsonSerializerOptions> options = new(() => CreateDefaultOptions());

        /// <summary>
        /// Provides a pre-configured instance of <see cref="JsonSerializerOptions"/> that aligns with the context's settings.
        /// </summary>
        public static JsonSerializerOptions DefaultOptions { get => options.Value; }

        [UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "DefaultJsonTypeInfoResolver is only used when reflection-based serialization is enabled")]
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026", Justification = "DefaultJsonTypeInfoResolver is only used when reflection-based serialization is enabled")]
        static JsonSerializerOptions CreateDefaultOptions()
        {
            JsonSerializerOptions options = new(Default.Options)
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true,
            };

            if (JsonSerializer.IsReflectionEnabledByDefault)
            {
                // If reflection-based serialization is enabled by default, use it as a fallback for all other types.
                // Also turn on string-based enum serialization for all unknown enums.
                options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
                options.Converters.Add(new JsonStringEnumConverter());
            }

            options.MakeReadOnly();
            return options;
        }
    }

    [Description("Entities are the primary nodes in the knowledge graph.")]
    record Entity(
        [Description("A unique name (identifier)")]
        string Name,
        [Description("An entity type (e.g., \"person\", \"organization\", \"event\")")]
        string Type,
        [Description(
            """
            Observations are discrete pieces of information about an entity. They are:

            * Stored as strings
            * Attached to specific entities
            * Can be added or removed independently
            * Should be atomic (one fact per observation)

            Example:
            {
              "name": "John_Smith",
              "observations": [
                "Speaks fluent Spanish",
                "Graduated in 2019",
                "Prefers morning meetings"
              ]
            }
            """)]
        string[] Observations);

    [Description(
        """
        Relations define directed connections between entities. 
        They are always stored in active voice and describe how entities interact or relate to each other.

        Example:
        {
          "from": "John_Smith",
          "to": "Anthropic",
          "type": "works_at"
        }
        """)]
    record Relation(string From, string To, string Type);

    record Observation(
        [Description("Target entity of this observation")]
        string Entity,
        [Description("New observations to add")]
        string[] Contents);

    record KnowledgeGraph(Entity[] Entities, Relation[] Relations);
}
