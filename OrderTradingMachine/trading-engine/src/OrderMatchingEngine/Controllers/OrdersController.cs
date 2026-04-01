using Microsoft.AspNetCore.Mvc;
using OrderMatchingEngine.Models;
using OrderMatchingEngine.Services;
using Shared.Models;
using Swashbuckle.AspNetCore.Annotations;

namespace OrderMatchingEngine.Controllers;

[ApiController]
[Route("api/orders")]
[Produces("application/json")]
public class OrdersController : ControllerBase
{
    private readonly IOrderMatchingService _service;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(IOrderMatchingService service, ILogger<OrdersController> logger)
    {
        _service = service;
        _logger = logger;
    }


    /// <param name="req">Order details</param>
    /// <param name="ct">Cancellation token</param>
    [HttpPost("submit")]
    [SwaggerOperation(
        Summary = "Submit New Order (NEW)",
        Description = "Submit a new limit order. Returns order details and any immediate matches."
    )]
    [SwaggerResponse(201, "Order created successfully", typeof(NewOrderResponse))]
    [SwaggerResponse(400, "Invalid request - check symbol, side, price, or quantity")]
    [ProducesResponseType(typeof(NewOrderResponse), 201)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> NewOrder([FromBody] NewOrderRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        if (!Enum.TryParse<OrderSide>(req.Side.ToUpper(), out var side))
            return BadRequest(new { error = "Side must be BUY or SELL" });

        var (order, trades) = await _service.NewOrderAsync(req.Symbol, side, req.Price, req.Quantity, ct);

        return Created($"/api/orders/{order.OrderId}", new NewOrderResponse
        {
            OrderId = order.OrderId,
            Symbol = order.Symbol,
            Side = order.Side.ToString(),
            Price = order.Price,
            Quantity = order.Quantity,
            RemainingQuantity = order.RemainingQuantity,
            Status = order.Status.ToString(),
            Trades = trades.Select(t => new TradeDto
            {
                TradeId = t.TradeId,
                Price = t.Price,
                Quantity = t.Quantity,
                ExecutedAt = t.ExecutedAt
            }).ToList()
        });
    }


    /// <param name="orderId">The unique order ID to cancel</param>
    /// <param name="ct">Cancellation token</param>
    [HttpDelete("cancel/{orderId}")]
    [SwaggerOperation(
        Summary = "Cancel Order (CANCEL)",
        Description = "Cancel an existing order by ID. Returns error if order is already filled or cancelled."
    )]
    [SwaggerResponse(200, "Order cancelled successfully")]
    [SwaggerResponse(404, "Order not found or cannot be cancelled")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> CancelOrder(string orderId, CancellationToken ct)
    {
        var success = await _service.CancelOrderAsync(orderId, ct);
        if (!success) return NotFound(new { error = $"Order {orderId} not found or already terminal" });
        return Ok(new { orderId, status = "CANCELLED" });
    }


    /// <param name="orderId">The unique order ID to modify</param>
    /// <param name="req">Modification details (price/quantity)</param>
    /// <param name="ct">Cancellation token</param>
    [HttpPatch("modify/{orderId}")]
    [SwaggerOperation(
        Summary = "Modify Order (MODIFY)",
        Description = "Modify price and/or quantity of an existing order. May trigger immediate matches."
    )]
    [SwaggerResponse(200, "Order modified successfully", typeof(ModifyOrderResponse))]
    [SwaggerResponse(400, "Invalid modification request")]
    [SwaggerResponse(404, "Order not found or cannot be modified")]
    [ProducesResponseType(typeof(ModifyOrderResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ModifyOrder(string orderId, [FromBody] ModifyOrderRequest req, CancellationToken ct)
    {
        if (req.NewPrice is null && req.NewQuantity is null)
            return BadRequest(new { error = "Provide at least newPrice or newQuantity" });

        var (success, trades) = await _service.ModifyOrderAsync(orderId, req.NewPrice, req.NewQuantity, ct);
        if (!success) return NotFound(new { error = $"Order {orderId} not found, already terminal, or invalid modification" });

        return Ok(new ModifyOrderResponse
        {
            OrderId = orderId,
            Trades = trades.Select(t => new TradeDto
            {
                TradeId = t.TradeId,
                Price = t.Price,
                Quantity = t.Quantity,
                ExecutedAt = t.ExecutedAt
            }).ToList()
        });
    }


    /// <param name="orderId">The unique order ID</param>
    [HttpGet("find/{orderId}")]
    [SwaggerOperation(
        Summary = "Find Particular Order Id",
        Description = "Get a snapshot of the order book for a particular order id."
    )]
    [SwaggerResponse(200, "Order found", typeof(Order))]
    [SwaggerResponse(404, "Order not found")]
    [ProducesResponseType(typeof(Order), 200)]
    [ProducesResponseType(404)]
    public IActionResult GetOrder(string orderId)
    {
        var order = _service.GetOrder(orderId);
        if (order is null) return NotFound();
        return Ok(order);
    }

    /// <param name="symbol">Stock symbol</param>
    [HttpGet("book/{symbol}")]
    [SwaggerOperation(
        Summary = "Get Order Book (PRINT)",
        Description = "Get a snapshot of the order book for a symbol with all bids and asks."
    )]
    [SwaggerResponse(200, "Order book snapshot")]
    [SwaggerResponse(404, "No order book exists for this symbol")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public IActionResult PrintBook(string symbol)
    {
        var snapshot = _service.PrintBook(symbol);
        if (snapshot is null) return NotFound(new { error = $"No book found for {symbol}" });

        return Ok(new
        {
            snapshot.Symbol,
            snapshot.Timestamp,
            Bids = snapshot.Bids.Select(kvp => new
            {
                Price = kvp.Key,
                Orders = kvp.Value.Select(o => new { o.OrderId, o.Quantity, o.RemainingQuantity, o.Status, o.CreatedAt })
            }),
            Asks = snapshot.Asks.Select(kvp => new
            {
                Price = kvp.Key,
                Orders = kvp.Value.Select(o => new { o.OrderId, o.Quantity, o.RemainingQuantity, o.Status, o.CreatedAt })
            })
        });
    }

    /// <summary>GET all active symbols</summary>
    [HttpGet("symbols")]
    [SwaggerOperation(
        Summary = "Get all the symbols"
    )]
    public IActionResult GetSymbols() => Ok(_service.GetActiveSymbols());
}
