using System.Collections.Concurrent;
using Shared.Events;
using Shared.Infrastructure;
using Shared.Models;

namespace OrderMatchingEngine.Services;

public interface IOrderMatchingService
{
    Task<(Order Order, List<Trade> Trades)> NewOrderAsync(string symbol, OrderSide side, decimal price, int quantity, CancellationToken ct = default);
    Task<bool> CancelOrderAsync(string orderId, CancellationToken ct = default);
    Task<(bool Success, List<Trade> Trades)> ModifyOrderAsync(string orderId, decimal? newPrice, int? newQuantity, CancellationToken ct = default);
    OrderBookSnapshot? PrintBook(string symbol);
    Order? GetOrder(string orderId);
    IEnumerable<string> GetActiveSymbols();
}

public class OrderMatchingService : IOrderMatchingService
{
    private readonly ConcurrentDictionary<string, OrderBook> _books = new();
    private readonly ConcurrentDictionary<string, Order> _globalOrders = new();
    private readonly IEventBus _eventBus;
    private readonly ILogger<OrderMatchingService> _logger;

    public OrderMatchingService(IEventBus eventBus, ILogger<OrderMatchingService> logger)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<(Order Order, List<Trade> Trades)> NewOrderAsync(
        string symbol, OrderSide side, decimal price, int quantity, CancellationToken ct = default)
    {
        var order = new Order(Guid.NewGuid().ToString(), symbol, side, price, quantity);
        var book = _books.GetOrAdd(symbol, s => new OrderBook(s));

        _globalOrders[order.OrderId] = order;
        var trades = book.AddOrder(order);

        _logger.LogInformation("NEW {Side} {Qty} at {Price} [{Symbol}] - {Trades} trades",
            side, quantity, price, symbol, trades.Count);

        // Publish trade events asynchronously (producer-consumer)
        foreach (var trade in trades)
        {
            await _eventBus.PublishAsync(new TradeExecutedEvent
            {
                TradeId = trade.TradeId,
                Symbol = trade.Symbol,
                BuyOrderId = trade.BuyOrderId,
                SellOrderId = trade.SellOrderId,
                Price = trade.Price,
                Quantity = trade.Quantity,
                ExecutedAt = trade.ExecutedAt
            }, ct);

            _logger.LogInformation("Event published for {Side} {Qty} at {Price} [{Symbol}] - {Trades} trades",
            side, quantity, price, symbol, trades.Count);
        }

        return (order, trades);
    }

    public async Task<bool> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        if (!_globalOrders.TryGetValue(orderId, out var order)) return false;
        var book = _books.GetOrAdd(order.Symbol, s => new OrderBook(s));
        var result = book.CancelOrder(orderId);

        if (result)
            _logger.LogInformation("CANCEL order {OrderId}", orderId);

        return result;
    }

    public async Task<(bool Success, List<Trade> Trades)> ModifyOrderAsync(
        string orderId, decimal? newPrice, int? newQuantity, CancellationToken ct = default)
    {
        if (!_globalOrders.TryGetValue(orderId, out var order)) return (false, []);
        var book = _books.GetOrAdd(order.Symbol, s => new OrderBook(s));
        var (success, trades) = book.ModifyOrder(orderId, newPrice, newQuantity);

        if (success)
        {
            _logger.LogInformation("MODIFY order {OrderId} price={Price} qty={Qty}",
                orderId, newPrice, newQuantity);

            foreach (var trade in trades)
            {
                await _eventBus.PublishAsync(new TradeExecutedEvent
                {
                    TradeId = trade.TradeId,
                    Symbol = trade.Symbol,
                    BuyOrderId = trade.BuyOrderId,
                    SellOrderId = trade.SellOrderId,
                    Price = trade.Price,
                    Quantity = trade.Quantity,
                    ExecutedAt = trade.ExecutedAt
                }, ct);
            }
        }

        return (success, trades);
    }

    public OrderBookSnapshot? PrintBook(string symbol) =>
        _books.TryGetValue(symbol, out var book) ? book.GetSnapshot() : null;

    public Order? GetOrder(string orderId) =>
        _globalOrders.TryGetValue(orderId, out var o) ? o : null;

    public IEnumerable<string> GetActiveSymbols() => _books.Keys;
}
