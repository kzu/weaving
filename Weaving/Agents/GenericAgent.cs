using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Weaving.Agents;

[Service]
public class GenericAgent([FromKeyedServices("generic")] IChatClient client, Lazy<IEnumerable<IAgent>> agents) : IAgent
{
    public string Name => "generic_agent";

    public string Capabilities =>
        """
        This is a generic agent to be used only as a fallback when no other more specific agent is available.
        It can handle generic conversation.
        """;

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellation = default)
    {
        var system = JsonSerializer.Serialize(agents.Value, JsonOptions.Default);
        return client.GetResponseAsync([new ChatMessage(ChatRole.System, system), .. messages], new ChatOptions
        {
            RawRepresentationFactory = (_) => new GrokCompletionOptions(),
        }, cancellationToken: cancellation);
    }

    class GrokCompletionOptions : OpenAI.Chat.ChatCompletionOptions
    {
        [JsonPropertyName("search_parameters")]
        public IDictionary<string, object> SearchParameters { get; set; } = new Dictionary<string, object>
        {
            { "mode", "auto" }
        };
    }
}
