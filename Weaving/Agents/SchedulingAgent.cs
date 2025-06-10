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
public class SchedulingAgent : ConfigurableAgent
{
    readonly Scheduler scheduler;
    readonly IMessageBus bus;
    readonly ILogger<IAgent> logger;
    readonly ChatMessage systemMessage;

    public SchedulingAgent(IServiceProvider services,
        Scheduler scheduler, IMessageBus bus, ILogger<IAgent> logger)
        : base(services, "agents:scheduler")
    {
        this.scheduler = scheduler;
        this.bus = bus;
        this.logger = logger;
        systemMessage = new(ChatRole.System, Prompt);

        Options.Tools ??= [];
        Options.Tools.AddRange(
        [
            AIFunctionFactory.Create(() => DateTimeOffset.Now, "get_date", "Gets the current date time (with offset)."),
            AIFunctionFactory.Create(SchedulePrompt),
            AIFunctionFactory.Create(ScheduleRelativeTime)
        ]);
    }

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellation = default)
    {
        var messagesJson = JsonSerializer.Serialize(messages, JsonOptions.Default);
        var response = await Client.GetResponseAsync(
            [
                systemMessage,
                new ChatMessage(ChatRole.User,
                $"""
                Given the following user conversation: 
                {messagesJson}

                Invoke the relevant tools to schedule a prompt for execution at the relevant time.
                """)
            ], Options, cancellation);

        return await Client.GetResponseAsync([systemMessage, .. messages], Options, cancellation);
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

        scheduler.Schedule(async () => bus.Notify(await Client.GetResponseAsync(prompt, Options)), dateTime - DateTimeOffset.Now, recurring: false);
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

        scheduler.Schedule(async () => bus.Notify(await Client.GetResponseAsync(prompt, Options)), delay, recurring);
        logger.LogInformation("{when} -> {prompt}", delay.Humanize(), prompt);

        FunctionInvokingChatClient.CurrentContext?.Terminate = true;
    }
}