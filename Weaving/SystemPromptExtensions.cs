using System;
using System.Linq;
using Microsoft.Extensions.AI;

namespace Weaving;

public static class SystemPromptExtensions
{
    extension(ChatOptions options)
    {
        public string? SystemPrompt
        {
            get => (options.AdditionalProperties ??= []).TryGetValue("SystemPrompt", out var value) ? value as string : null;
            set => (options.AdditionalProperties ??= [])["SystemPrompt"] = value;
        }
    }

    public static ChatClientBuilder UseSystemPrompt(this ChatClientBuilder builder)
    {
        _ = Throw.IfNull(builder);

        return builder.Use((messages, options, inner, cancellation) =>
        {
            if (options?.SystemPrompt is { Length: > 0 } prompt &&
                !messages.Any(x => x.Role == ChatRole.System && x.Text == prompt))
            {
                messages = [new ChatMessage(ChatRole.System, prompt), .. messages];
            }
            return inner(messages, options, cancellation);
        });
    }
}
