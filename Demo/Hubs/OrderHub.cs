using Microsoft.AspNetCore.SignalR;

namespace Demo.Hubs
{
    // No server-side methods are needed here — the browser never *calls* this
    // hub, it only *listens*. All the "OrderPlaced" / "OrderUpdated" messages
    // are pushed from OrderController/CheckoutController/PaymentController via
    // IHubContext<OrderHub>.
    public class OrderHub : Hub
    {
    }
}
