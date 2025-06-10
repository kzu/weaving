using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Weaving.Agents;

/// <summary>
/// An agent is essentially like a sub-chat client implementation that uses (potentially) 
/// the same incoming history/messages from the user, adds its own (system/assistant) messages, 
/// and produces a response. The agent coordinator can then use the response to build up 
/// the final response to the user from the outer workflow.
/// </summary>
public interface IAgent
{
    string Name { get; }
    string Description { get; }
    Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellation = default);
}
