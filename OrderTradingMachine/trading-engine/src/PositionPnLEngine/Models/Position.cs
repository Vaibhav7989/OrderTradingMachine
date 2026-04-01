namespace PositionPnLEngine.Models;

/// <summary>
/// Tracks a single symbol's position with running average cost and PnL.
/// 
/// Average price formula (FIFO-style running average):
///   When buying:  avgPrice = (netQty * avgPrice + fillQty * fillPrice) / (netQty + fillQty)
///   When selling: avgPrice stays the same; realized PnL = fillQty * (fillPrice - avgPrice)
/// </summary>
public class Position
{
    private readonly object _lock = new();

    public string Symbol { get; }
    public int NetQuantity { get; private set; }       // positive = long, negative = short
    public decimal AveragePrice { get; private set; }
    public decimal RealizedPnL { get; private set; }
    public decimal LastMarketPrice { get; private set; }
    public DateTime LastUpdated { get; private set; } = DateTime.UtcNow;

    public decimal UnrealizedPnL =>
        NetQuantity == 0 || LastMarketPrice == 0
            ? 0m
            : NetQuantity * (LastMarketPrice - AveragePrice);

    public decimal TotalPnL => RealizedPnL + UnrealizedPnL;

    public Position(string symbol) => Symbol = symbol;

    /// <summary>Process a fill (FILL command). Thread-safe.</summary>
    public void ApplyFill(string side, int quantity, decimal price)
    {
        lock (_lock)
        {
            if (side.Equals("BUY", StringComparison.OrdinalIgnoreCase))
                ApplyBuy(quantity, price);
            else
                ApplySell(quantity, price);

            LastUpdated = DateTime.UtcNow;
        }
    }

    /// <summary>Update market price (PRICE command). Thread-safe.</summary>
    public void UpdateMarketPrice(decimal price)
    {
        lock (_lock)
        {
            LastMarketPrice = price;
            LastUpdated = DateTime.UtcNow;
        }
    }

    public PositionSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new PositionSnapshot
            {
                Symbol = Symbol,
                NetQuantity = NetQuantity,
                AveragePrice = AveragePrice,
                LastMarketPrice = LastMarketPrice,
                RealizedPnL = RealizedPnL,
                UnrealizedPnL = UnrealizedPnL,
                TotalPnL = TotalPnL,
                Timestamp = LastUpdated
            };
        }
    }

    private void ApplyBuy(int qty, decimal price)
    {
        if (NetQuantity >= 0)
        {
            // Adding to long position: update running average
            AveragePrice = (NetQuantity * AveragePrice + qty * price) / (NetQuantity + qty);
            NetQuantity += qty;
        }
        else
        {
            // Covering a short position
            int coveredQty = Math.Min(qty, -NetQuantity);
            RealizedPnL += coveredQty * (AveragePrice - price); // short PnL: sell high, buy low
            NetQuantity += qty;

            if (NetQuantity > 0)
            {
                // Flipped to long — remainder establishes new long at fill price
                AveragePrice = price;
            }
            else if (NetQuantity == 0)
            {
                AveragePrice = 0;
            }
        }
    }

    private void ApplySell(int qty, decimal price)
    {
        if (NetQuantity <= 0)
        {
            // Adding to short position
            AveragePrice = ((-NetQuantity) * AveragePrice + qty * price) / ((-NetQuantity) + qty);
            NetQuantity -= qty;
        }
        else
        {
            // Reducing long position
            int reducedQty = Math.Min(qty, NetQuantity);
            RealizedPnL += reducedQty * (price - AveragePrice); // long PnL: sell high, bought low
            NetQuantity -= qty;

            if (NetQuantity < 0)
            {
                // Flipped to short
                AveragePrice = price;
            }
            else if (NetQuantity == 0)
            {
                AveragePrice = 0;
            }
        }
    }
}

public record PositionSnapshot
{
    public string Symbol { get; init; } = string.Empty;
    public int NetQuantity { get; init; }
    public decimal AveragePrice { get; init; }
    public decimal LastMarketPrice { get; init; }
    public decimal RealizedPnL { get; init; }
    public decimal UnrealizedPnL { get; init; }
    public decimal TotalPnL { get; init; }
    public DateTime Timestamp { get; init; }
}
