namespace Shared.Models;

public enum OrderSide { BUY, SELL }
public enum OrderStatus { OPEN, PARTIALLY_FILLED, FILLED, CANCELLED }

public record Order
{
    public string OrderId { get; init; } = Guid.NewGuid().ToString();
    public string Symbol { get; init; } = string.Empty;
    public OrderSide Side { get; init; }
    public decimal Price { get; init; }
    public int Quantity { get; init; }
    public int RemainingQuantity { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.OPEN;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Order() { }

    public Order(string orderId, string symbol, OrderSide side, decimal price, int quantity)
    {
        OrderId = orderId;
        Symbol = symbol;
        Side = side;
        Price = price;
        Quantity = quantity;
        RemainingQuantity = quantity;
        Status = OrderStatus.OPEN;
        CreatedAt = DateTime.UtcNow;
    }
}

public record Trade
{
    public string TradeId { get; init; } = Guid.NewGuid().ToString();
    public string Symbol { get; init; } = string.Empty;
    public string BuyOrderId { get; init; } = string.Empty;
    public string SellOrderId { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int Quantity { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.UtcNow;
}
