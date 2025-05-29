using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Merq;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console;
using Spectre.Console.Json;

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
    List<ChatMessage> messages = [];
    bool showJson;

    public Interactive(IChatClient chat, ChatOptions options, IMessageBus bus)
    {
        this.bus = bus;
        this.chat = chat;
        chatOptions = options.Clone();
        chatOptions.ModelId = "claude-sonnet-4-20250514";
        chatOptions.MaxOutputTokens = 10000;
        chatOptions.Temperature = 0.7f;
        chatOptions.Tools ??= [];
        chatOptions.Tools.Add(AIFunctionFactory.Create(ClearOutput));

        InitializeHistory();

        bus.Observe<ChatResponse>().Subscribe(AddResponse);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        showJson = AnsiConsole.Confirm("Do you want to view a JSON render of the responses from the AI?");
        _ = Task.Run(ListenAsync, cts.Token);
        return Task.CompletedTask;
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
        InitializeHistory();
        AnsiConsole.MarkupLine($":robot: Cleared :broom:");
    }

    void InitializeHistory()
    {
        if (chatOptions.SystemPrompt is { } system)
            messages = [new ChatMessage(ChatRole.System, system)];
        else
            messages = [];
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
                var response = await AnsiConsole.Status().StartAsync(":robot: Thinking...",
                    ctx => chat.GetResponseAsync([.. messages, input.AsChat()], chatOptions, cts.Token));

                messages.Add(new ChatMessage(ChatRole.Assistant, $"User said '{input}'"));
                AddResponse(response);
            }
        }
    }

    void AddResponse(ChatResponse response)
    {
        messages.AddRange(response.Messages.Where(x => x.Contents.All(y => y is TextContent)));

        try
        {
            AnsiConsole.Write(new Markup($":robot: {response.Text}"));
        }
        catch (Exception e)
        {
            AnsiConsole.MarkupLine("[red] Failed to write response text as markup[/]");
            AnsiConsole.WriteException(e);
            AnsiConsole.MarkupInterpolated($":robot: {response.Text}");
        }

        AnsiConsole.WriteLine();

        if (showJson)
        {
            AnsiConsole.Write(new Panel(new JsonText(JsonSerializer.Serialize(response, jsonOptions))));
            AnsiConsole.WriteLine();
        }

        AnsiConsole.Markup($":person_beard: ");
    }
}
