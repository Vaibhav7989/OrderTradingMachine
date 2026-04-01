using System.Collections.Concurrent;
using Shared.Models;

namespace OrderMatchingEngine.Services;

/// <summary>
/// Thread-safe, price-time priority order book per symbol.
/// BUY side: sorted descending (highest bid first).
/// SELL side: sorted ascending (lowest ask first).
/// Uses ReaderWriterLockSlim for high read / lower write throughput.
/// </summary>
public class OrderBook
{
    private readonly string _symbol;

    // BUY: SortedDictionary<price DESC, Queue<order>> — highest price first
    private readonly SortedDictionary<decimal, Queue<Order>> _bids =
        new(Comparer<decimal>.Create((a, b) => b.CompareTo(a)));

    // SELL: SortedDictionary<price ASC, Queue<order>> — lowest price first
    private readonly SortedDictionary<decimal, Queue<Order>> _asks =
        new(Comparer<decimal>.Create((a, b) => a.CompareTo(b)));

    private readonly ConcurrentDictionary<string, Order> _ordersById = new();
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

    public OrderBook(string symbol) => _symbol = symbol;

    public IReadOnlyDictionary<string, Order> AllOrders => _ordersById;

    /// <summary>Adds an order and returns any trades generated.</summary>
    public List<Trade> AddOrder(Order order)
    {
        _lock.EnterWriteLock();
        try
        {
            _ordersById[order.OrderId] = order;
            var trades = Match(order);

            // If order still has remaining qty, rest it in the book
            if (order.RemainingQuantity > 0 && order.Status != OrderStatus.CANCELLED)
            {
                var book = order.Side == OrderSide.BUY ? _bids : _asks;
                if (!book.TryGetValue(order.Price, out var queue))
                {
                    queue = new Queue<Order>();
                    book[order.Price] = queue;
                }
                queue.Enqueue(order);
            }

            return trades;
        }
        finally { _lock.ExitWriteLock(); }
    }

    public bool CancelOrder(string orderId)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!_ordersById.TryGetValue(orderId, out var order)) return false;
            if (order.Status is OrderStatus.FILLED or OrderStatus.CANCELLED) return false;

            order.Status = OrderStatus.CANCELLED;
            order.UpdatedAt = DateTime.UtcNow;
            RemoveFromBook(order);
            return true;
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>Modify price or quantity. Returns new trades if re-priced causes a match.</summary>
    public (bool Success, List<Trade> Trades) ModifyOrder(string orderId, decimal? newPrice, int? newQuantity)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!_ordersById.TryGetValue(orderId, out var original)) return (false, []);
            if (original.Status is OrderStatus.FILLED or OrderStatus.CANCELLED) return (false, []);

            RemoveFromBook(original);

            // Create a modified copy (records are immutable, so we need a workaround)
            var filledQty = original.Quantity - original.RemainingQuantity;
            var newQty = newQuantity ?? original.Quantity;
            if (newQty <= filledQty) return (false, []); // Can't reduce below already filled

            var modified = new Order(original.OrderId, original.Symbol, original.Side,
                newPrice ?? original.Price, newQty)
            {
                RemainingQuantity = newQty - filledQty,
                Status = original.Status,
                UpdatedAt = DateTime.UtcNow
            };

            _ordersById[orderId] = modified;
            var trades = Match(modified);

            if (modified.RemainingQuantity > 0 && modified.Status != OrderStatus.CANCELLED)
            {
                var book = modified.Side == OrderSide.BUY ? _bids : _asks;
                if (!book.TryGetValue(modified.Price, out var queue))
                {
                    queue = new Queue<Order>();
                    book[modified.Price] = queue;
                }
                queue.Enqueue(modified);
            }

            return (true, trades);
        }
        finally { _lock.ExitWriteLock(); }
    }

    public OrderBookSnapshot GetSnapshot()
    {
        _lock.EnterReadLock();
        try
        {
            return new OrderBookSnapshot
            {
                Symbol = _symbol,
                Bids = _bids.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.ToList()),
                Asks = _asks.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.ToList()),
                Timestamp = DateTime.UtcNow
            };
        }
        finally { _lock.ExitReadLock(); }
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private List<Trade> Match(Order incoming)
    {
        var trades = new List<Trade>();
        var oppositeBook = incoming.Side == OrderSide.BUY ? _asks : _bids;

        while (incoming.RemainingQuantity > 0 && oppositeBook.Count > 0)
        {
            var bestLevel = oppositeBook.First();
            var bestPrice = bestLevel.Key;

            bool isMatch = incoming.Side == OrderSide.BUY
                ? incoming.Price >= bestPrice
                : incoming.Price <= bestPrice;

            if (!isMatch) break;

            var queue = bestLevel.Value;
            while (queue.Count > 0 && incoming.RemainingQuantity > 0)
            {
                var resting = queue.Peek();
                if (resting.Status == OrderStatus.CANCELLED)
                {
                    queue.Dequeue();
                    continue;
                }

                int fillQty = Math.Min(incoming.RemainingQuantity, resting.RemainingQuantity);
                decimal fillPrice = bestPrice; // Resting order's price (price-time priority)

                var trade = new Trade
                {
                    Symbol = _symbol,
                    BuyOrderId = incoming.Side == OrderSide.BUY ? incoming.OrderId : resting.OrderId,
                    SellOrderId = incoming.Side == OrderSide.SELL ? incoming.OrderId : resting.OrderId,
                    Price = fillPrice,
                    Quantity = fillQty,
                    ExecutedAt = DateTime.UtcNow
                };
                trades.Add(trade);

                incoming.RemainingQuantity -= fillQty;
                resting.RemainingQuantity -= fillQty;
                resting.UpdatedAt = incoming.UpdatedAt = DateTime.UtcNow;

                incoming.Status = incoming.RemainingQuantity == 0
                    ? OrderStatus.FILLED : OrderStatus.PARTIALLY_FILLED;
                resting.Status = resting.RemainingQuantity == 0
                    ? OrderStatus.FILLED : OrderStatus.PARTIALLY_FILLED;

                if (resting.RemainingQuantity == 0) queue.Dequeue();
            }

            if (queue.Count == 0) oppositeBook.Remove(bestPrice);
        }

        return trades;
    }

    private void RemoveFromBook(Order order)
    {
        var book = order.Side == OrderSide.BUY ? _bids : _asks;
        if (!book.TryGetValue(order.Price, out var queue)) return;

        // Rebuild queue without the cancelled order (O(n) at this level, acceptable for book depth)
        var items = queue.Where(o => o.OrderId != order.OrderId).ToList();
        queue.Clear();
        foreach (var o in items) queue.Enqueue(o);
        if (queue.Count == 0) book.Remove(order.Price);
    }
}

public class OrderBookSnapshot
{
    public string Symbol { get; init; } = string.Empty;
    public Dictionary<decimal, List<Order>> Bids { get; init; } = [];
    public Dictionary<decimal, List<Order>> Asks { get; init; } = [];
    public DateTime Timestamp { get; init; }
}
