using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Weaving.Agents;

public abstract class ConfigurableAgent : IAgent
{
    readonly Lazy<IChatClient> client;

    protected ConfigurableAgent(IServiceProvider services, string section)
        : this(services, services.GetRequiredService<IConfiguration>(), section) { }

    protected ConfigurableAgent(IServiceProvider services, IConfiguration configuration, string section)
    {
        if (section.Contains('.'))
            throw new ArgumentException("Section separator must be ':', not '.'");

        // NOTE: this will already bind the Options property too if it exists as a subsection.
        configuration.GetRequiredSection(section).Bind(this, x => x.BindNonPublicProperties = true);

        Throw.IfNullOrEmpty(Name, $"{section}:name must be set in configuration.");
        Throw.IfNullOrEmpty(Description, $"{section}:description must be set in configuration.");

        ClientId = Throw.IfNullOrEmpty(
            configuration[$"{section}:client"],
            $"{section}:client must be set in configuration.");

        this.client = new Lazy<IChatClient>(() =>
            services.GetRequiredKeyedService<IChatClient>(ClientId),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string Name { get; protected set; }

    public string Description { get; protected set; }

    public string? Prompt { get; protected set; }

    public ChatOptions Options { get; } = new();

    protected IChatClient Client => client.Value;

    protected string ClientId { get; }

    public virtual Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellation = default)
        => Task.FromResult(new ChatResponse());
}
