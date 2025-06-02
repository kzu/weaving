using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Spectre.Console;
using Spectre.Console.Json;

namespace Weaving;

public static class ConsoleLoggingExtensions
{
    public static ChatClientBuilder UseConsoleLogging(this ChatClientBuilder builder)
    {
        _ = Throw.IfNull(builder);

        return builder.Use((innerClient, services) => new ConsoleLoggingChatClient(innerClient));
    }

    class ConsoleLoggingChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
    {
        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            AnsiConsole.Write(new Panel(new JsonText(new
            {
                messages = messages.Where(x => x.Role != ChatRole.System).ToArray(),
                options
            }.ToJsonString())));

            var response = await InnerClient.GetResponseAsync(messages, options, cancellationToken);

            AnsiConsole.Write(new Panel(new JsonText(response.ToJsonString())));
            return response;
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            AnsiConsole.Write(new Panel(new JsonText(new
            {
                messages = messages.Where(x => x.Role != ChatRole.System).ToArray(),
                options
            }.ToJsonString())));

            List<ChatResponseUpdate> updates = [];

            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
            {
                updates.Add(update);
                yield return update;
            }

            AnsiConsole.Write(new Panel(new JsonText(updates.ToJsonString())));
        }
    }
}
