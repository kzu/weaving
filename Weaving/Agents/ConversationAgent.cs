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
public class ConversationAgent(IServiceProvider services, Lazy<IEnumerable<IAgent>> agents) : ConfigurableAgent(services, "agents:conversation")
{
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellation = default)
    {
        var system = JsonSerializer.Serialize(agents.Value, JsonOptions.Default);
        return Client.GetResponseAsync([new ChatMessage(ChatRole.System, Prompt!.Replace("{agents", system)), .. messages], new ChatOptions
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
