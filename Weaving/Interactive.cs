using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Devlooped;
using Merq;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console;

namespace Weaving;

[Service]
public class Interactive : IHostedService
{
    readonly CancellationTokenSource cts = new();

    readonly IServiceProvider services;
    readonly IMessageBus bus;
    readonly Lazy<ChatOptions> chatOptions;
    readonly CloudStorageAccount storage;
    IChatClient? chat;
    string endUserId;

    public Interactive(IServiceProvider services, IConfiguration configuration, Lazy<ChatOptions> options, IMessageBus bus, CloudStorageAccount storage)
    {
        this.services = services;
        this.bus = bus;
        this.storage = storage;
        endUserId = configuration["EndUserId"] ?? Environment.UserName;

        chatOptions = new Lazy<ChatOptions>(() =>
        {
            var chatOptions = options.Value.Clone();

            chatOptions.MaxOutputTokens = 10000;
            chatOptions.Temperature = 0.7f;
            chatOptions.Tools ??= [];
            chatOptions.Tools.Add(AIFunctionFactory.Create(ClearOutput));

            // We add it also to the outer/global options so that it can be used in the system prompt
            //if (!chatOptions.Tools.Any(x => x.Name == "get_date"))
            //    chatOptions.Tools.Add(AIFunctionFactory.Create(() => DateTimeOffset.Now, "get_date", "Gets the current date time (with offset)."));

            return chatOptions;
        });

        bus.Observe<ChatResponse>().Subscribe(AddResponse);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var user = AnsiConsole.Ask("Enter your name", endUserId);
        chatOptions.Value.EndUserId = user;

        // Prompt user to select LLM provider
        var provider = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select an LLM provider:")
                .AddChoices(["claude", "openai"])
        );

        chat = services.GetRequiredKeyedService<IChatClient>(provider);
        if (provider == "claude")
            chatOptions.Value.ModelId = "claude-sonnet-4-20250514";

        if (AnsiConsole.Confirm("Do you want to resume the last conversation?", false))
        {
            var table = storage.CreateTableServiceClient().GetTableClient("Weaving");
            var row = table.Query<TableEntity>(x => x.PartitionKey == "Conversations", cancellationToken: cts.Token)
                .OrderByDescending(x => x.Timestamp)
                .Take(1)
                .FirstOrDefault();

            if (row is not null)
            {
                chatOptions.Value.ConversationId = row.RowKey;
                AnsiConsole.MarkupLine($":robot: Resuming conversation with ID [bold]{row.RowKey}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine(":robot: No previous conversations found, starting a new one.");
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
        chatOptions.Value.ConversationId = null;
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
                        ctx => chat!.GetResponseAsync(input, chatOptions.Value, cts.Token));

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
