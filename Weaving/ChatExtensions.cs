using System;
using System.ClientModel;
using System.Linq;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

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

    /// <summary>
    /// Provides a standard chat client configuration.
    /// </summary>
    public static ChatClientBuilder UseStandardChat(this ChatClientBuilder builder, string userId, bool consoleLogging) => builder
        .Use((messages, options, next, cancellation) =>
        {
            (options ??= new()).EndUserId = userId;
            return next(messages, options, cancellation);
        })
        .UseConversationStorage()
        //.UseMemory()
        //.UseGraphMemory()
        //.UseSystemPrompt()
        //.UseAgents()
        .UseFunctionInvocation()
        .UseLogging()
        .UseConsoleLogging(consoleLogging);

    public static IServiceCollection UseOpenAI(this IServiceCollection services, IConfiguration configuration,
        string userId, bool consoleLogging, Action<string, ChatClientBuilder>? configure = default)
    {
        foreach (var entry in configuration.AsEnumerable().Where(x =>
            x.Key.StartsWith("openai", StringComparison.OrdinalIgnoreCase) &&
            x.Key.EndsWith("model", StringComparison.OrdinalIgnoreCase)))
        {
            var section = string.Join(':', entry.Key.Split(':')[..^1]);
            var id = configuration[$"{section}:id"] ?? section.Split(':')[^1].ToLowerInvariant();
            var builder = services.AddKeyedChatClient(id, services =>
            {
                var config = services.GetRequiredService<IConfiguration>();
                var options = config.GetSection(section).Get<ConfigurableOptions>()
                    ?? throw new InvalidOperationException($"Failed to bind {section} configuration section to {nameof(OpenAIClientOptions)}.");

                var key = options.Key;
                var keysection = section;
                while (string.IsNullOrEmpty(key))
                {
                    keysection = string.Join(':', keysection.Split(':')[..^1]);
                    if (string.IsNullOrEmpty(keysection))
                        break;
                    key = config[$"{keysection}:key"];
                }

                Throw.IfNullOrEmpty(key, $"Missing OpenAI API key in configuration for {id}.");
                Throw.IfNullOrEmpty(options.Model, $"Missing Model in section {section}.");

                return new OpenAIClient(new ApiKeyCredential(key), options)
                    .GetChatClient(options.Model).AsIChatClient();
            });
            configure?.Invoke(id, builder);
            builder.UseStandardChat(userId, consoleLogging);
        }

        return services;
    }

    class ConfigurableOptions : OpenAIClientOptions
    {
        public required string Id { get; set; }
        public required string Key { get; set; }
        public required string Model { get; set; }
    }
}
