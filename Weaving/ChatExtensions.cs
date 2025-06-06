using Microsoft.Extensions.AI;

namespace Weaving;

public static class ChatExtensions
{
    extension(ChatOptions options)
    {
        public string? EndUserId
        {
            get => (options.AdditionalProperties ??= []).TryGetValue("EndUserId", out var value) ? value as string : null;
            set => (options.AdditionalProperties ??= [])["EndUserId"] = value;
        }
    }
}
