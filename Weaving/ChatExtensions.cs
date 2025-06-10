using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using OpenAI.Chat;

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

    public static IServiceCollection UseOpenAI(this IServiceCollection collection, IConfiguration configuration,
        string userId, bool consoleLogging, Action<string, ChatClientBuilder>? configure = default)
    {
        foreach (var entry in configuration.AsEnumerable().Where(x =>
            x.Key.StartsWith("openai", StringComparison.OrdinalIgnoreCase) &&
            x.Key.EndsWith("model", StringComparison.OrdinalIgnoreCase)))
        {
            var section = string.Join(':', entry.Key.Split(':')[..^1]);
            var id = configuration[$"{section}:id"] ?? section.Split(':')[^1].ToLowerInvariant();

            collection.AddKeyedSingleton(id, (services, id) =>
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

                if (options.Endpoint is not null && options.Endpoint.Host == "api.x.ai")
                    return new GrokAIClient(new ApiKeyCredential(key), options);

                return new OpenAIClient(new ApiKeyCredential(key), options);
            });

            var builder = collection.AddKeyedChatClient(id, services =>
            {
                var config = services.GetRequiredService<IConfiguration>();
                var options = config.GetSection(section).Get<ConfigurableOptions>()
                    ?? throw new InvalidOperationException($"Failed to bind {section} configuration section to {nameof(OpenAIClientOptions)}.");

                Throw.IfNullOrEmpty(options.Model, $"Missing Model in section {section}.");

                return services
                    .GetRequiredKeyedService<OpenAIClient>(id)
                    .GetChatClient(options.Model)
                    .AsIChatClient();
            });

            configure?.Invoke(id, builder);
            builder.UseStandardChat(userId, consoleLogging);
        }

        return collection;
    }

    class ConfigurableOptions : OpenAIClientOptions
    {
        public required string Id { get; set; }
        public required string Key { get; set; }
        public required string Model { get; set; }
    }

    class GrokAIClient(ApiKeyCredential credential, ConfigurableOptions options) : OpenAIClient(credential, options)
    {
        public override ChatClient GetChatClient(string model) => new GrokChatClient(Pipeline, options);

        class GrokChatClient(ClientPipeline pipeline, ConfigurableOptions configuration) : ChatClient(pipeline, configuration.Model, configuration)
        {
            public override async Task<ClientResult<ChatCompletion>> CompleteChatAsync(IEnumerable<OpenAI.Chat.ChatMessage> messages, ChatCompletionOptions? options = null, CancellationToken cancellationToken = default)
            {
                var message = Pipeline.CreateMessage();
                message.Request.Method = "POST";
                message.Request.Uri = new Uri(configuration.Endpoint, "/v1/chat/completions");

                var request = new GrokChatRequest(messages, configuration.Model, SearchMode.Auto);
                var binaryData = request.Write(ModelReaderWriterOptions.Json);
                message.Request.Content = BinaryContent.Create(binaryData);
                message.Request.Headers.Add("Content-Type", "application/json");

                await Pipeline.SendAsync(message);
                Debug.Assert(message.Response != null);

                var json = message.Response.Content.ToString();
                var result = ClientResult.FromResponse(message.Response);
                var completion = (ChatCompletion)result;
                var typed = ClientResult.FromValue(completion, message.Response);

                return typed;
            }

            public override Task<ClientResult<ChatCompletion>> CompleteChatAsync(params OpenAI.Chat.ChatMessage[] messages) => CompleteChatAsync(messages.AsEnumerable(), null, CancellationToken.None);
            public override Task<ClientResult> CompleteChatAsync(BinaryContent content, RequestOptions? options = null) => throw new NotSupportedException();
            public override ClientResult CompleteChat(BinaryContent content, RequestOptions? options = null) => throw new NotSupportedException();
            public override ClientResult<ChatCompletion> CompleteChat(IEnumerable<OpenAI.Chat.ChatMessage> messages, ChatCompletionOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public override ClientResult<ChatCompletion> CompleteChat(params OpenAI.Chat.ChatMessage[] messages) => throw new NotSupportedException();
            public override CollectionResult<StreamingChatCompletionUpdate> CompleteChatStreaming(IEnumerable<OpenAI.Chat.ChatMessage> messages, ChatCompletionOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public override CollectionResult<StreamingChatCompletionUpdate> CompleteChatStreaming(params OpenAI.Chat.ChatMessage[] messages) => throw new NotSupportedException();
            public override AsyncCollectionResult<StreamingChatCompletionUpdate> CompleteChatStreamingAsync(IEnumerable<OpenAI.Chat.ChatMessage> messages, ChatCompletionOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public override AsyncCollectionResult<StreamingChatCompletionUpdate> CompleteChatStreamingAsync(params OpenAI.Chat.ChatMessage[] messages) => throw new NotSupportedException();
        }

        enum SearchMode
        {
            Auto,
            On,
            Off
        }

        class GrokChatRequest : IJsonModel<GrokChatRequest>
        {
            public IEnumerable<OpenAI.Chat.ChatMessage> Messages { get; }
            public string Model { get; }
            public SearchMode SearchMode { get; }

            public GrokChatRequest(IEnumerable<OpenAI.Chat.ChatMessage> messages, string model, SearchMode searchMode)
            {
                Messages = messages;
                Model = model;
                SearchMode = searchMode;
            }

            void IJsonModel<GrokChatRequest>.Write(Utf8JsonWriter writer, ModelReaderWriterOptions options)
            {
                writer.WriteStartObject();

                writer.WritePropertyName("messages");
                writer.WriteStartArray();
                foreach (var msg in Messages)
                {
                    var msgData = ModelReaderWriter.Write(msg, options);
                    using var doc = JsonDocument.Parse(msgData);
                    doc.RootElement.WriteTo(writer);
                }
                writer.WriteEndArray();

                writer.WritePropertyName("search_parameters");
                writer.WriteStartObject();
                writer.WritePropertyName("mode");
                writer.WriteStringValue(SearchMode.ToString().ToLowerInvariant());
                writer.WriteEndObject();

                writer.WritePropertyName("model");
                writer.WriteStringValue(Model);

                writer.WriteEndObject();
            }

            public BinaryData Write(ModelReaderWriterOptions options) => ModelReaderWriter.Write(this, options);
            public string GetFormatFromOptions(ModelReaderWriterOptions options) => "J"; // "J" for JSON, as in the built-in models
            public GrokChatRequest Create(BinaryData data, ModelReaderWriterOptions options) => throw new NotImplementedException();
            GrokChatRequest IJsonModel<GrokChatRequest>.Create(ref Utf8JsonReader reader, ModelReaderWriterOptions options) => throw new NotImplementedException();
        }
    }
}
