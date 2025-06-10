using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Weaving.Agents;

public record Plan(Step[] Steps);

public record Step(string Id, string Agent, string[] DependsOn);

//[Service]
public class AgentCoordinator(IChatClient client, IEnumerable<IAgent> agents, ILogger<AgentCoordinator> logger)
{
    const string SystemPrompt =
        """
        You are a coordinator for an intelligent bot that uses various agents to achieve user-driven tasks. 
        Given a user's request and a list of available agents with their capabilities, your job is to create 
        a plan specifying which agents to call, in what order, and the dependencies between them.

        Available agents and their capabilities:

        {{agents}}

        User's conversation:
        
        {{messages}}

        Based on the user's messages:
        1. Identify the necessary tasks to fulfill the request.
        2. Map each task to the appropriate agent based on its capabilities.
        3. Determine the sequence of tasks, considering dependencies between tasks.

        Provide a structured plan in JSON format with the following structure:

        {
          "steps": [
            {
              "id": "memory",
              "agent": "memory_retrieval",
            },
            {
              "id": "scheduler",
              "agent": "tasks_scheduler",
              "depends_on": ["memory"]
            },
            {
              "id": "email",
              "agent": "email_sender",
              "depends_on": ["memory", "scheduler"]
            },
            {
              "id": "persister",
              "agent": "memory_storage",
              "depends_on": ["memory"]
            }
          ]
        }
        
        Rules:
        - If tasks must be executed in a specific order due to dependencies, set "depends_on" appropriately. 
        - Make sure to generate unique yet friendly identifiers for each step
        - List them in the correct sequence.
        - If no agents can handle a task, return a single step with the generic agent.
        """;

    readonly string agentsJson = JsonSerializer.Serialize(agents, JsonOptions.Default);
    readonly IDictionary<string, IAgent> agentsMap = agents.ToDictionary(a => a.Name);

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellation = default)
    {
        var messagesJson = JsonSerializer.Serialize(messages, JsonOptions.Default);
        var systemPrompt = SystemPrompt
            .Replace("{{agents}}", agentsJson)
            .Replace("{{messages}}", messagesJson);

        // Get a plan from the LLM
        var planResponse = await client.GetResponseAsync<Plan>(new ChatMessage(ChatRole.User, systemPrompt), options, true, cancellation);
        var plan = planResponse.Result;

        logger.LogTrace("Execution plan: \r{plan}", JsonSerializer.Serialize(plan, JsonOptions.Default));

        var stepResults = new Dictionary<string, ChatResponse>();
        var stepTasks = new Dictionary<string, Task<ChatResponse>>();
        var stepCompletions = plan.Steps.ToDictionary(s => s.Id, s => new TaskCompletionSource<ChatResponse>());

        foreach (var step in plan.Steps)
        {
            stepTasks[step.Id] = Task.Run(async () =>
            {
                var dependencyMessages = new List<ChatMessage>(messages);
                if (step.DependsOn != null)
                {
                    foreach (var depId in step.DependsOn)
                    {
                        if (stepCompletions.TryGetValue(depId, out var depTcs))
                        {
                            var depResult = await depTcs.Task;
                            if (depResult?.Messages != null)
                            {
                                dependencyMessages.AddRange(depResult.Messages);
                            }
                        }
                    }
                }

                if (!agentsMap.TryGetValue(step.Agent, out var agent))
                    throw new InvalidOperationException($"Agent '{step.Agent}' not found for step '{step.Id}'.");

                var response = await agent.GetResponseAsync(dependencyMessages, cancellation);
                stepResults[step.Id] = response;
                stepCompletions[step.Id].SetResult(response);
                return response;

            }, cancellation);
        }

        await Task.WhenAll(stepTasks.Values);

        var finalMessages = new List<ChatMessage>();

        // We'll accumulate messages in the order of the plan steps.
        // This assumes plan steps are ordered in a way that makes sense for accumulation.
        // Or, we might need a more sophisticated way to determine the final message order.
        foreach (var step in plan.Steps)
        {
            if (stepResults.TryGetValue(step.Id, out var result) && result?.Messages != null)
            {
                finalMessages.AddRange(result.Messages);
            }
        }

        // For simplicity, we are creating a new ChatResponse. 
        // Depending on requirements, you might want to aggregate other properties from individual agent responses.
        return new ChatResponse(finalMessages);
    }
}
