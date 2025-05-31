using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Devlooped;
using Merq;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console;

namespace Weaving;

[Service]
public class Interactive : IHostedService
{
    static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    readonly CancellationTokenSource cts = new();

    readonly IMessageBus bus;
    readonly IChatClient chat;
    readonly ChatOptions chatOptions;
    readonly CloudStorageAccount storage;

    public Interactive(IChatClient chat, ChatOptions options, IMessageBus bus, CloudStorageAccount storage)
    {
        this.bus = bus;
        this.chat = chat;
        this.storage = storage;
        chatOptions = options.Clone();
        chatOptions.ModelId = "claude-sonnet-4-20250514";
        chatOptions.MaxOutputTokens = 10000;
        chatOptions.Temperature = 0.7f;
        chatOptions.Tools ??= [];
        chatOptions.Tools.Add(AIFunctionFactory.Create(ClearOutput));

        // We add it also to the outer/global options so that it can be used in the system prompt
        if (!chatOptions.Tools.Any(x => x.Name == "get_date"))
            chatOptions.Tools.Add(AIFunctionFactory.Create(() => DateTimeOffset.Now, "get_date", "Gets the current date time (with offset)."));

        bus.Observe<ChatResponse>().Subscribe(AddResponse);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (AnsiConsole.Confirm("Do you want to resume the last conversation?"))
        {
            await foreach (var entry in TablePartition.Create(storage, "Weaving", "Conversations").EnumerateAsync())
            {
                chatOptions.ConversationId = entry.RowKey;
                break;
            }
        }

        _ = Task.Run(ListenAsync, cts.Token);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cts.Cancel();
        AnsiConsole.MarkupLine($":robot: Stopping");
        return Task.CompletedTask;
    }

    [Description("Clears the console/messages rendered so far. Only call if the user explicitly asked for this function to be executed.")]
    void ClearOutput()
    {
        AnsiConsole.Clear();
        chatOptions.ConversationId = null;
        AnsiConsole.MarkupLine($":robot: Cleared :broom:");
    }

    async Task ListenAsync()
    {
        AnsiConsole.MarkupLine($":robot: Ready");
        AnsiConsole.Markup($":person_beard: ");
        while (!cts.IsCancellationRequested)
        {
            var input = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(input))
            {
                try
                {
                    var response = await AnsiConsole.Status().StartAsync(":robot: Thinking...",
                        ctx => chat.GetResponseAsync(input, chatOptions, cts.Token));

                    AddResponse(response);
                }
                catch (Exception e)
                {
                    AnsiConsole.WriteException(e);
                }
            }
        }
    }

    void AddResponse(ChatResponse response)
    {
        try
        {
            if (response.Text is { Length: > 0 })
                AnsiConsole.MarkupLine($":robot: {response.Text}");
        }
        catch (Exception e)
        {
            AnsiConsole.MarkupLine("[red] Failed to write response text as markup[/]");
            AnsiConsole.WriteException(e);
            AnsiConsole.MarkupInterpolated($":robot: {response.Text}");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Markup($":person_beard: ");
    }
}
