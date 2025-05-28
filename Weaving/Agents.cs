using System;
using ModelContextProtocol.Server;

namespace Weaving;


public interface IAgent
{
}

[McpServerToolType]
public class SchedulingAgent : IAgent
{
    [McpServerTool]
    public void ScheduleTask(string taskName)
    {
        // Implementation for scheduling a task
        Console.WriteLine($"Task '{taskName}' has been scheduled.");
    }
}