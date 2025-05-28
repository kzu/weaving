using System;
using Anthropic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateApplicationBuilder(args);

#if DEBUG
host.Environment.EnvironmentName = "Development";
host.Configuration.AddUserSecrets<Program>();
#endif

host.Services.AddServices();

host.Services.AddChatClient(new AnthropicClient(host.Configuration["Claude:Key"]
    ?? throw new InvalidOperationException("Missing Claude:Key configuration.")));

//host.Services.AddLogging(config => config.AddConsole());

var app = host.Build();

await app.RunAsync();
