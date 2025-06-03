using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.AI;

namespace Weaving;

public static class SystemPromptExtensions
{
    extension(ChatOptions options)
    {
        public void AddSystemPrompt(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return;

            options.SystemPrompts ??= [];
            options.SystemPrompts?.Add(prompt);
        }

        public HashSet<string>? SystemPrompts
        {
            get => (options.AdditionalProperties ??= []).TryGetValue("SystemPrompts", out var value) ? value as HashSet<string> : null;
            set => (options.AdditionalProperties ??= [])["SystemPrompts"] = value;
        }
    }

    public static ChatClientBuilder UseSystemPrompt(this ChatClientBuilder builder)
    {
        _ = Throw.IfNull(builder);

        return builder.Use((messages, options, inner, cancellation) =>
        {
            if (options?.SystemPrompts is { Count: > 0 } prompts)
            {
                foreach (var prompt in prompts)
                {
                    if (!messages.Any(x => x.Role == ChatRole.System && x.Text == prompt))
                        messages = [new ChatMessage(ChatRole.System, prompt), .. messages];
                }
            }
            return inner(messages, options, cancellation);
        });
    }
}
