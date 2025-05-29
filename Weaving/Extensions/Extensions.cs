using System;
using System.Collections.Generic;
using Microsoft.Extensions.AI;

namespace Weaving;

static class Extensions
{
    extension(string prompt)
    {
        // TODO: should be an implicit conversion operator.
        public ChatMessage AsChat(ChatRole? role = default) => new(role ?? ChatRole.User, prompt);
    }

    extension(ChatOptions options)
    {
        public string? SystemPrompt
        {
            get => (options.AdditionalProperties ??= []).TryGetValue("SystemPrompt", out var value) ? value as string : null;
            set => (options.AdditionalProperties ??= [])["SystemPrompt"] = value;
        }
    }

    extension<T>(IList<T> list)
    {
        public void AddRange(IEnumerable<T> items)
        {
            ArgumentNullException.ThrowIfNull(items);
            foreach (var item in items)
            {
                list.Add(item);
            }
        }
    }
}
