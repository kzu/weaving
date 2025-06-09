using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Merq;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Weaving;

namespace Weaving.Agents;

[Service]
public class SchedulingAgent : IAgent
{
    readonly IChatClient chat;
    readonly Scheduler scheduler;
    readonly IMessageBus bus;
    readonly ILogger<IAgent> logger;
    readonly ChatOptions options;

    const string SystemPrompt =
        """
        You are an agent that performs time-related jobs, such as scheduling prompts to be 
        executed at a future time or after a delay. 
        """;
    static readonly ChatMessage systemMessage = new(ChatRole.System, SystemPrompt);

    public SchedulingAgent(
        [FromKeyedServices("scheduler")] IChatClient chat,
        [FromKeyedServices("scheduler")] ChatOptions options,
        Scheduler scheduler, IMessageBus bus, ILogger<IAgent> logger)
    {
        this.chat = chat;
        this.scheduler = scheduler;
        this.bus = bus;
        this.logger = logger;

        this.options = options.Clone();
        options.Tools ??= [];
        options.Tools.AddRange(
        [
            AIFunctionFactory.Create(() => DateTimeOffset.Now, "get_date", "Gets the current date time (with offset)."),
            AIFunctionFactory.Create(SchedulePrompt),
            AIFunctionFactory.Create(ScheduleRelativeTime)
        ]);
    }

    public string Name => "schedule_task";

    public string Capabilities =>
        """
        This agent can perform time-related jobs, such as scheduling prompts to be executed at a future time or after a delay.
        The prompt should contain the instructions on the task to be performed, and the agent will execute it when specified in the prompt.
        If the prompt does NOT contain specific date or time instructions, it should NOT be called.
        """;

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellation = default)
    {
        var messagesJson = JsonSerializer.Serialize(messages, JsonOptions.Default);
        var response = await chat.GetResponseAsync(
            [
                systemMessage,
                new ChatMessage(ChatRole.User,
                $"""
                Given the following user conversation: 
                {messagesJson}

                Invoke the relevant tools to schedule a prompt for execution at the relevant time.
                """)
            ], options, cancellation);

        return await chat.GetResponseAsync([systemMessage, .. messages], options, cancellation);
    }

    [Description("Schedules execution of a given prompt for some future time.")]
    [McpServerTool]
    public void SchedulePrompt(
        [Description("The prompt to execute at a future time.")] string prompt,
        [Description("The absolute time in the future when this prompt should be executed. Use ISO8601 roundtripable format for the date.")] DateTimeOffset dateTime)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        if (dateTime < DateTimeOffset.Now)
            throw new ArgumentOutOfRangeException(nameof(dateTime), "The date time must be in the future.");

        scheduler.Schedule(async () => bus.Notify(await chat.GetResponseAsync(prompt, options)), dateTime - DateTimeOffset.Now, recurring: false);
        logger.LogInformation("{when} -> {prompt}", dateTime.Humanize(), prompt);

        FunctionInvokingChatClient.CurrentContext?.Terminate = true;
    }

    [Description("Schedules execution (optionally recurring) of a given prompt after a delay.")]
    [McpServerTool]
    public void ScheduleRelativeTime(
        [Description("The prompt to execute at a future time.")] string prompt,
        [Description("The delay to wait before the prompt should be executed. It must be formatted in HH:mm:ss so it can be converted to a .NET TimeSpan type.")] TimeSpan delay,
        [Description("Whether the action should be executed in a recurring fashion after each delay has elapsed")] bool recurring)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        scheduler.Schedule(async () => bus.Notify(await chat.GetResponseAsync(prompt, options)), delay, recurring);
        logger.LogInformation("{when} -> {prompt}", delay.Humanize(), prompt);

        FunctionInvokingChatClient.CurrentContext?.Terminate = true;
    }
}