using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Gremlin.Net.Driver;
using Gremlin.Net.Structure.IO.GraphSON;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Spectre.Console;

namespace Weaving;

public static partial class GraphMemoryExtensions
{
    const string SystemPrompt =
        """
        # Knowledge Graph Memory Management:
        
        Follow these steps for each interaction:
        
        1. Memory Retrieval:
           - Always begin your chat by saying only "Remembering..." and retrieve all relevant information from your knowledge graph
           - Always refer to your knowledge graph as your "memory"
           - Always retrieve user preferences before making any kind of recommendations about any topic
        
        2. Memory
           - While conversing with the user, be attentive to any new information that falls into these categories:
             a) Basic Identity (age, gender, location, job title, education level, etc.)
             b) Behaviors (interests, habits, etc.)
             c) Preferences (communication style, preferred language, etc.)
             d) Goals (goals, targets, aspirations, etc.)
             e) Relationships (personal and professional relationships up to 3 degrees of separation)
        
        3. Memory Update:
           - If any new information was gathered during the interaction, update your memory as follows:
             a) Create entities for recurring organizations, people, and significant events
             b) Connect them to the current entities using relations
             b) Store facts about them as observations

        Never refer to the implementation details of graph databases, graphs, vertices, edges, properties, etc.
        They should all be referred to as "memory" or "knowledge".
        """;

    const string GremlinDescription =
            """
            Executes an arbitrary Gremlin Query Language request against Azure Cosmos DB using JSON format for the output.
            The request scripts MUST always filter vertices and edges by a property named `tenantId` with a value 
            of `$$partitionKey$$` which is used as a variable substitution reference.

            Use the graph mutation steps to create, modify, and delete vertices and edges. 
            Use the graph traversal steps to query the graph.
            All created vertices and edges must include a property named `tenantId` with a value of `$$partitionKey$$`. 
            This property is used to partition the data. Failing to include this variable will result in rejection of the request.
                        
            #Supported Graph Objects

            The following table lists the TinkerPop features that are implemented by Azure Cosmos DB:

            | Category                 | Azure Cosmos DB implementation                                                                 | Notes                                                                 |
            |--------------------------|-----------------------------------------------------------------------------------------------|----------------------------------------------------------------------|
            | Graph features           | Provides Persistence and ConcurrentAccess. Designed to support Transactions                   | Computer methods can be implemented via the Spark connector.          |
            | Variable features        | Supports Boolean, Integer, Byte, Double, Float, Long, String                                   | Supports primitive types, is compatible with complex types via data model |
            | Vertex features          | Supports RemoveVertices, MetaProperties, AddVertices, MultiProperties, StringIds, UserSuppliedIds, AddProperty, RemoveProperty | Supports creating, modifying, and deleting vertices                   |
            | Vertex property features | StringIds, UserSuppliedIds, AddProperty, RemoveProperty, BooleanValues, ByteValues, DoubleValues, FloatValues, IntegerValues, LongValues, StringValues | Supports creating, modifying, and deleting vertex properties          |
            | Edge features            | AddEdges, RemoveEdges, StringIds, UserSuppliedIds, AddProperty, RemoveProperty                 | Supports creating, modifying, and deleting edges                      |
            | Edge property features   | Properties, BooleanValues, ByteValues, DoubleValues, FloatValues, IntegerValues, LongValues, StringValues | Supports creating, modifying, and deleting edge properties            |

            # Gremlin steps
            Now let's look at the Gremlin steps supported by Azure Cosmos DB.
            
            | step                           | Description                                                                 |
            |--------------------------------|-----------------------------------------------------------------------------|
            | `addE`                        | Adds an edge between two vertices                                           |
            | `addV`                        | Adds a vertex to the graph                                                  |
            | `and`                         | Ensures that all the traversals return a value                               |
            | `as`                          | A step modulator to assign a variable to the output of a step                |
            | `by`                          | A step modulator used with `group` and `order`                              |
            | `coalesce`                    | Returns the first traversal that returns a result                            |
            | `constant`                    | Returns a constant value. Used with `coalesce`                               |
            | `count`                       | Returns the count from the traversal                                         |
            | `dedup`                       | Returns the values with the duplicates removed                               |
            | `drop`                        | Drops the values (vertex/edge)                                              |
            | `executionProfile`            | Creates a description of all operations generated by the executed Gremlin step |
            | `fold`                        | Acts as a barrier that computes the aggregate of results                     |
            | `group`                       | Groups the values based on the labels specified                             |
            | `has`                         | Used to filter properties, vertices, and edges. Supports `hasLabel`, `hasId`, `hasNot`, and `has` variants. |
            | `inject`                      | Inject values into a stream                                                 |
            | `is`                          | Used to perform a filter using a boolean expression                         |
            | `limit`                       | Used to limit number of items in the traversal                               |
            | `local`                       | Local wraps a section of a traversal, similar to a subquery                  |
            | `not`                         | Used to produce the negation of a filter                                    |
            | `optional`                    | Returns the result of the specified traversal if it yields a result else it returns the calling element |
            | `or`                          | Ensures at least one of the traversals returns a value                       |
            | `order`                       | Returns results in the specified sort order                                 |
            | `path`                        | Returns the full path of the traversal                                      |
            | `project`                     | Projects the properties as a Map                                            |
            | `properties`                  | Returns the properties for the specified labels                             |
            | `range`                       | Filters to the specified range of values                                    |
            | `repeat`                      | Repeats the step for the

             specified number of times. Used for looping |
            | `sample`                      | Used to sample results from the traversal                                   |
            | `select`                      | Used to project results from the traversal                                  |
            | `store`                       | Used for non-blocking aggregates from the traversal                         |
            | `TextP.startingWith(string)`  | String filtering function. This function is used as a predicate for the `has()` step to match a property with the beginning of a given string |
            | `TextP.endingWith(string)`    | String filtering function. This function is used as a predicate for the `has()` step to match a property with the ending of a given string |
            | `TextP.containing(string)`    | String filtering function. This function is used as a predicate for the `has()` step to match a property with the contents of a given string |
            | `TextP.notStartingWith(string)` | String filtering function. This function is used as a predicate for the `has()` step to match a property that doesn't start with a given string |
            | `TextP.notEndingWith(string)` | String filtering function. This function is used as a predicate for the `has()` step to match a property that doesn't end with a given string |
            | `TextP.notContaining(string)` | String filtering function. This function is used as a predicate for the `has()` step to match a property that doesn't contain a given string |
            | `tree`                        | Aggregate paths from a vertex into a tree                                   |
            | `unfold`                      | Unroll an iterator as a step                                               |
            | `union`                       | Merge results from multiple traversals                                      |
            | `V`                           | Includes the steps necessary for traversals between vertices and edges `V`, `E`, `out`, `in`, `both`, `outE`, `inE`, `bothE`, `outV`, `inV`, `bothV`, and `otherV` |
            | `where`                       | Used to filter results from the traversal. Supports `eq`, `neq`, `lt`, `lte`, `gt`, `gte`, and `between` operators |

            The write-optimized engine provided by Azure Cosmos DB supports automatic indexing of all properties within vertices and edges by default. Therefore, queries with filters, range queries, sorting, or aggregates on any property are processed from the index, and served efficiently. For more information on how indexing works in Azure Cosmos DB, see our paper on schema-agnostic indexing.

            # Unsupported features

            - **Gremlin Bytecode** is a programming language agnostic specification for graph traversals. Azure Cosmos DB Graph doesn't support it yet. Use `GremlinClient.SubmitAsync()` and pass traversal as a text string.
            - **`property(set, 'xyz', 1)`** set cardinality isn't supported today. Use `property(list, 'xyz', 1)` instead. 
            - The **`match()` step** isn't currently available. This step provides declarative querying capabilities.
            - **Objects as properties** on vertices or edges aren't supported. Properties can only be primitive types or arrays.
            - **Sorting by array properties** `order().by(<array property>)` isn't supported. Sorting is supported only by primitive types.
            - **Non-primitive JSON types** aren't supported. Use `string`, `number`, or `true`/`false` types. `null` values aren't supported.
            - **GraphSONv3** serializer isn't currently supported. Use `GraphSONv2` Serializer, Reader, and Writer classes in the connection configuration. The results returned by the Azure Cosmos DB for Gremlin don't have the same format as the GraphSON format.
            - **Lambda expressions and functions** aren't currently supported. This includes the `.map{<expression>}`, the `.by{<expression>}`, and the `.filter{<expression>}` functions. To learn more, and to learn how to rewrite them using Gremlin steps, see [A Note on Lambdas](http://tinkerpop.apache.org/docs/current/reference/#a-note-on-lambdas).
            - **Transactions** aren't supported because of distributed nature of the system. Configure appropriate consistency model on Gremlin account to "read your own writes" and use optimistic concurrency to resolve conflicting writes.

            # Known limitations
            
            - Index utilization for Gremlin queries with mid-traversal .V() steps: Currently, only the first .V() call of a traversal will make use of the index to resolve any filters or predicates attached to it. Subsequent calls will not consult the index, which might increase the latency and cost of the query.

            Assuming default indexing, a typical read Gremlin query that starts with the .V() step would use parameters in its attached filtering steps, such as .has() or .where() to optimize the cost and performance of the query. For example:

            ```
            g.V().has('category', 'A')
            ```
            However, when more than one .V() step is included in the Gremlin query, the resolution of the data for the query might not be optimal. Take the following query as an example:
            ```
            g.V().has('category', 'A').as('a').V().has('category', 'B').as('b').select('a', 'b')
            ```
            This query will return two groups of vertices based on their property called category. In this case, only the first call, g.V().has('category', 'A') will make use of the index to resolve the vertices based on the values of their properties.

            A workaround for this query is to use subtraversal steps such as .map() and union(). This is exemplified below:
                        ```            
            g.V().has('category', 'A').as('a').map(__.V().has('category', 'B')).as('b').select('a','b')

            // Query workaround using .union()
            g.V().has('category', 'A').fold().union(unfold(), __.V().has('category', 'B'))
            ```
            """;

    public static ChatClientBuilder UseGraphMemory(this ChatClientBuilder builder, ILoggerFactory? loggerFactory = null)
    {
        _ = Throw.IfNull(builder);

        return builder.Use((inner, services) =>
        {
            loggerFactory ??= services.GetRequiredService<ILoggerFactory>();

            return new GraphMemoryChatClient(
                services.GetRequiredService<IConfiguration>(),
                loggerFactory.CreateLogger<GraphMemoryChatClient>(),
                inner);
        });
    }

    partial class GraphMemoryChatClient : DelegatingChatClient
    {
        static readonly AsyncLocal<string?> userId = new();
        ConnectionPoolSettings connectionPoolSettings = new ConnectionPoolSettings()
        {
            MaxInProcessPerConnection = 32,
            PoolSize = 4,
            //ReconnectionAttempts = 3,
            //ReconnectionBaseDelay = TimeSpan.FromMilliseconds(500)
        };

        Lazy<GremlinServer> server;
        Lazy<GremlinClient> client;
        GraphOptions options;
        ILogger logger;
        readonly AsyncRetryPolicy retryPolicy;

        public GraphMemoryChatClient(IConfiguration configuration, ILogger<GraphMemoryChatClient> logger, IChatClient inner) : base(inner)
        {
            this.logger = logger;
            options = configuration.GetSection("Graph").Get<GraphOptions>() ?? new GraphOptions();
            server = new(() => new GremlinServer(options.Host!, 443, enableSsl: true,
                username: $"/dbs/{options.Database}/colls/{options.Container}",
                password: options.Key));

            client = new(() => new GremlinClient(
                server.Value,
                new GraphSON2MessageSerializer(),
                connectionPoolSettings,
                socket => socket.KeepAliveInterval = TimeSpan.FromSeconds(30)));

            retryPolicy = Policy
                .Handle<WebSocketException>(IsTransientWebSocketError)
                .Or<Gremlin.Net.Driver.Exceptions.ResponseException>(ex => ex.StatusCode == Gremlin.Net.Driver.Messages.ResponseStatusCode.TooManyRequests)
                .Or<TimeoutException>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1)),
                    onRetryAsync: (exception, timeSpan, attempt, context) =>
                    {
                        logger.LogWarning(exception, "Transient error on attempt {Attempt}/3, retrying after {TimeSpan}ms", attempt, timeSpan.TotalMilliseconds);
                        return Task.CompletedTask;
                    });
        }

        // Identify transient WebSocket errors
        bool IsTransientWebSocketError(WebSocketException ex) =>
            ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely ||
            ex.WebSocketErrorCode == WebSocketError.Faulted ||
            ex.InnerException is TimeoutException;

        public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            userId.Value = options?.EndUserId;

            SetupTools(options);

            return base.GetResponseAsync(messages, options, cancellationToken);
        }

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            userId.Value = options?.EndUserId;

            SetupTools(options);

            return base.GetStreamingResponseAsync(messages, options, cancellationToken);
        }

        void SetupTools(ChatOptions? options)
        {
            if (options?.EndUserId is { Length: > 0 } userId)
            {
                options.AddSystemPrompt(SystemPrompt.Replace("$$endUserId$$", userId));
                options.Tools ??= [];
                var tools = options.Tools;
                if (!options.Tools.Any(x => x.Name == "graph_query"))
                {
                    tools.Insert(0, AIFunctionFactory.Create(ExecuteQuery, "graph_mutate"));
                    tools.Insert(0, AIFunctionFactory.Create(ExecuteQuery, "graph_query"));
                }
                options.ToolMode = ChatToolMode.RequireAny;
            }
        }

        [Description(GremlinDescription)]
        Task<ResultSet<object>> QueryGraph(
            [Description("Query-only script that never mutates the graph.")]
            string queryScript) => ExecuteQuery(queryScript);

        [Description(GremlinDescription)]
        Task<ResultSet<object>> MutateGraph(
            [Description("Graph-mutation script.")]
            string mutationScript) => ExecuteQuery(mutationScript);

        async Task<ResultSet<object>> ExecuteQuery(
            [Description(
                """
                A complete Gremlin script to be evaluated by Azure CosmosDB. 
                The script MUST always filter vertices and edges by a property named `tenantId` with a value 
                of `$$partitionKey$$` which is used as a variable substitution reference.
                """)]
            string requestScript)
        {
            if (!requestScript.Contains("$$partitionKey$$", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("All Gremlin request scripts MUST always filter vertices and edges by a property named `tenantId` with a value of `$$partitionKey$$` which is used as a variable substitution reference.");

            try
            {
                logger.LogTrace("Executing Gremlin query: {Query}", requestScript);
                return await retryPolicy.ExecuteAsync(()
                    => client.Value.SubmitAsync<object>(requestScript.Replace("$$partitionKey$$", userId.Value)));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to execute Gremlin query: {Query}", requestScript);
                AnsiConsole.WriteException(ex);
                throw;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && client?.IsValueCreated == true)
                client.Value.Dispose();

            base.Dispose(disposing);
        }
    }
}
