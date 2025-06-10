using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Weaving.Agents;

[Service]
public class OrderAgent : ConfigurableAgent
{
    readonly ILogger<OrderAgent> logger;

    public OrderAgent(IServiceProvider services, ILogger<OrderAgent> logger)
        : base(services, "agents:order")
    {
        this.logger = logger;
        this.Options.Tools ??= [];
        this.Options.Tools.Add(AIFunctionFactory.Create(PreviewOrderAsync, "preview_order", "Previews an order for a customer."));
        this.Options.Tools.Add(AIFunctionFactory.Create(PlaceOrderAsync, "place_order", "Places an order for a customer."));
        //options.Tools.AddRange(
        //[
        //    AIFunctionFactory.Create(() => "Order placed successfully.", "place_order", "Places an order for a customer."),
        //    AIFunctionFactory.Create(() => "Order cancelled successfully.", "cancel_order", "Cancels an order for a customer.")
        //]);
    }

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellation = default)
    {
        var response = await Client.GetResponseAsync<MessageAction>([new ChatMessage(ChatRole.System,
            Prompt), ..messages], Options);

        if (response.Result != null)
        {
            if (!string.IsNullOrEmpty(response.Result.Number))
                return new ChatResponse(new ChatMessage(ChatRole.User, response.Result.Message)
                {
                    AdditionalProperties = new()
                    {
                        { "phone_number", response.Result.Number },
                        { "complete", response.Result.Complete }
                    }
                });
            else
                return new ChatResponse(new ChatMessage(ChatRole.User, response.Result.Message));
        }

        return response;
    }

    async Task<MessageAction> PreviewOrderAsync(List<Item> Order, string phoneNumber, CancellationToken cancellation = default)
    {
        await Task.CompletedTask;

        // Simulate placing an order
        foreach (var item in Order)
        {
            logger.LogInformation("Placing order for {Quantity} of {Product} to {PhoneNumber}", item.Quantity, item.Product, phoneNumber);
        }

        //FunctionInvokingChatClient.CurrentContext?.Terminate = true; // Terminate the function execution loop

        return new("Awaiting user confirmation.", phoneNumber);
    }

    async Task<MessageAction> PlaceOrderAsync(List<Item> Order, string phoneNumber, CancellationToken cancellation = default)
    {
        // Simulate placing an order
        foreach (var item in Order)
        {
            logger.LogInformation("Placing order for {Quantity} of {Product} to {PhoneNumber}", item.Quantity, item.Product, phoneNumber);
        }
        await Task.Delay(1000, cancellation); // Simulate async operation
        logger.LogInformation("Order placed successfully.");
        return new("Order placed successfully.", phoneNumber, true);
    }

    record Item(string Product, int Quantity);

    record MessageAction(string Message, string? Number, bool Complete = false);
}
