using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Weaving;

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