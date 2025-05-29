using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Merq;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Weaving;

public interface IAgent
{
    string Id { get; }
    string Capabilities { get; }
    Task Execute(string prompt);
}

[Service]
public class Scheduler
{
    readonly ConcurrentDictionary<Timer, object?> timers = new();

    public void Schedule(Func<Task> action, TimeSpan delay, bool recurring = false)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);

        Timer? timer = null;
        timer = new Timer(_ =>
        {
            try
            {
                action().GetAwaiter().GetResult();
            }
            finally
            {
                if (!recurring && timer is not null)
                {
                    timer.Dispose();
                    timers.TryRemove(timer, out _);
                }
            }
        }, null, delay, recurring ? delay : Timeout.InfiniteTimeSpan);

        timers.TryAdd(timer, null);
    }
}

[Service]
[McpServerToolType]
public class SchedulingAgent : IAgent
{
    readonly IChatClient chat;
    readonly Scheduler scheduler;
    readonly IMessageBus bus;
    readonly ILogger<IAgent> logger;
    readonly Lazy<ChatOptions> options;
    readonly Lazy<ChatMessage> system;

    public SchedulingAgent([FromKeyedServices("openai")] IChatClient chat, Lazy<ChatOptions> options, Scheduler scheduler, IMessageBus bus, ILogger<IAgent> logger)
    {
        this.chat = chat;
        this.scheduler = scheduler;
        this.bus = bus;
        this.logger = logger;

        this.options = new Lazy<ChatOptions>(() =>
        {
            var cloned = options.Value.Clone();
            cloned.Tools ??= [];
            cloned.Tools.AddRange(
            [
                AIFunctionFactory.Create(() => DateTimeOffset.Now, "get_date", "Gets the current date time (with offset)."),
                AIFunctionFactory.Create(SchedulePrompt),
                AIFunctionFactory.Create(ScheduleRelativeTime)
            ]);

            return cloned;
        });

        system = new Lazy<ChatMessage>(() => new ChatMessage(ChatRole.System, options.Value.SystemPrompt ??
            "You are a scheduling agent that can execute tasks at specified times."));
    }

    public string Id => "scheduling_agent";

    public string Capabilities =>
        """
        This agent can perform time-related jobs, such as scheduling prompts to be executed at a future time or after a delay.
        The prompt should contain the instructions on the task to be performed, and the agent will execute it when specified in the prompt.
        """;

    public Task Execute(string prompt) => chat.GetResponseAsync([system.Value, prompt.AsChat()], options.Value);

    [Description("Schedules execution of a given prompt for some future time.")]
    [McpServerTool]
    public void SchedulePrompt(
        [Description("The prompt to execute at a future time.")] string prompt,
        [Description("The absolute time in the future when this prompt should be executed. Use ISO8601 roundtripable format for the date.")] DateTimeOffset dateTime)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        if (dateTime < DateTimeOffset.Now)
            throw new ArgumentOutOfRangeException(nameof(dateTime), "The date time must be in the future.");

        scheduler.Schedule(async () => bus.Notify(await chat.GetResponseAsync([system.Value, prompt.AsChat()], options.Value)), dateTime - DateTimeOffset.Now, recurring: false);
        logger.LogInformation("{when} -> {prompt}", dateTime.Humanize(), prompt);
    }

    [Description("Schedules execution (optionally recurring) of a given prompt after a delay.")]
    [McpServerTool]
    public void ScheduleRelativeTime(
        [Description("The prompt to execute at a future time.")] string prompt,
        [Description("The delay to wait before the prompt should be executed. It must be formatted in HH:mm:ss so it can be converted to a .NET TimeSpan type.")] TimeSpan delay,
        [Description("Whether the action should be executed in a recurring fashion after each delay has elapsed")] bool recurring)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        scheduler.Schedule(async () => bus.Notify(await chat.GetResponseAsync([system.Value, prompt.AsChat()], options.Value)), delay, recurring);
        logger.LogInformation("{when} -> {prompt}", delay.Humanize(), prompt);
    }
}