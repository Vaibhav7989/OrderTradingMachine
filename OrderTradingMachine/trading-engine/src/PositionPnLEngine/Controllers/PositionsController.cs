using Microsoft.AspNetCore.Mvc;
using PositionPnLEngine.Models;
using PositionPnLEngine.Services;
using Shared.Infrastructure;
using Shared.Events;
using Swashbuckle.AspNetCore.Annotations;

namespace PositionPnLEngine.Controllers;

[ApiController]
[Route("api/positions")]
[Produces("application/json")]
public class PositionsController : ControllerBase
{
    private readonly IPositionService _positionService;
    private readonly IEventBus _eventBus;
    private readonly ILogger<PositionsController> _logger;

    public PositionsController(IPositionService positionService, IEventBus eventBus, ILogger<PositionsController> logger)
    {
        _positionService = positionService;
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <param name="req">Fill details</param>
    /// <param name="ct">Cancellation token</param>
    [HttpPost("fill")]
    [SwaggerOperation(
        Summary = "Record Trade Fill (FILL)",
        Description = "Record a trade fill to update position, average price, and realized PnL."
    )]
    [SwaggerResponse(200, "Fill processed successfully", typeof(PositionSnapshot))]
    [SwaggerResponse(400, "Invalid fill request")]
    [ProducesResponseType(typeof(PositionSnapshot), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> Fill([FromBody] FillRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        _positionService.ApplyFill(req.Symbol, req.Side, req.Quantity, req.Price);

        await _eventBus.PublishAsync(new TradeExecutedEvent
        {
            Symbol = req.Symbol,
            BuyOrderId = req.Side.Equals("BUY", StringComparison.OrdinalIgnoreCase) ? req.OrderId ?? "manual" : string.Empty,
            SellOrderId = req.Side.Equals("SELL", StringComparison.OrdinalIgnoreCase) ? req.OrderId ?? "manual" : string.Empty,
            Price = req.Price,
            Quantity = req.Quantity,
            ExecutedAt = DateTime.UtcNow
        }, ct);

        var snapshot = _positionService.GetPosition(req.Symbol);
        return Ok(snapshot);
    }

    /// <param name="req">Price update details</param>
    /// <param name="ct">Cancellation token</param>
    [HttpPost("price")]
    [SwaggerOperation(
        Summary = "Update Market Price (PRICE)",
        Description = "Update market price to recalculate unrealized PnL for a symbol."
    )]
    [SwaggerResponse(200, "Price updated successfully", typeof(PositionSnapshot))]
    [SwaggerResponse(400, "Invalid price update request")]
    [ProducesResponseType(typeof(PositionSnapshot), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> UpdatePrice([FromBody] PriceUpdateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        _positionService.UpdateMarketPrice(req.Symbol, req.Price);

        await _eventBus.PublishAsync(new MarketPriceUpdatedEvent
        {
            Symbol = req.Symbol,
            Price = req.Price
        }, ct);

        var snapshot = _positionService.GetPosition(req.Symbol);
        return Ok(snapshot);
    }

    /// <param name="symbol">Stock symbol</param>
    [HttpGet("print/{symbol}")]
    [SwaggerOperation(
        Summary = "Get Position (PRINT)",
        Description = "Get position details and PnL for a specific symbol."
    )]
    [SwaggerResponse(200, "Position found", typeof(PositionSnapshot))]
    [SwaggerResponse(404, "No position exists for this symbol")]
    [ProducesResponseType(typeof(PositionSnapshot), 200)]
    [ProducesResponseType(404)]
    public IActionResult GetPosition(string symbol)
    {
        var snapshot = _positionService.GetPosition(symbol.ToUpper());
        if (snapshot is null) return NotFound(new { error = $"No position found for {symbol}" });
        return Ok(snapshot);
    }


    [HttpGet("printAllPositions")]
    [SwaggerOperation(
        Summary = "Get All Positions (PRINT ALL)",
        Description = "Get all positions and aggregated PnL summary across all symbols."
    )]
    [SwaggerResponse(200, "All positions retrieved")]
    [ProducesResponseType(200)]
    public IActionResult GetAllPositions()
    {
        var positions = _positionService.GetAllPositions().ToList();
        return Ok(new
        {
            Positions = positions,
            Summary = new
            {
                TotalRealizedPnL = positions.Sum(p => p.RealizedPnL),
                TotalUnrealizedPnL = positions.Sum(p => p.UnrealizedPnL),
                TotalPnL = positions.Sum(p => p.TotalPnL)
            }
        });
    }
}
