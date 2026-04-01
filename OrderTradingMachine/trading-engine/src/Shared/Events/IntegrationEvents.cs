namespace Shared.Events;

/// <summary>
/// Published by OrderMatchingEngine when a trade is executed.
/// Consumed by PositionPnLEngine to update positions and PnL.
/// </summary>
public record TradeExecutedEvent
{
    public string TradeId { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string BuyOrderId { get; init; } = string.Empty;
    public string SellOrderId { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int Quantity { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.UtcNow;
}

public record MarketPriceUpdatedEvent
{
    public string Symbol { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}
