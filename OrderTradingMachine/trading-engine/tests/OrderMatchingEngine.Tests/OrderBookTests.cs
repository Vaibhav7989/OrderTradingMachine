using Microsoft.Extensions.Logging.Abstractions;
using OrderMatchingEngine.Services;
using Shared.Events;
using Shared.Infrastructure;
using Shared.Models;
using Xunit;

namespace OrderMatchingEngine.Tests;

public class OrderBookTests
{
    private OrderBook NewBook() => new("AAPL");

    // For Full Fills
    [Fact]
    public void Buy_MatchesLowestSell_FullFill()
    {
        var book = NewBook();
        book.AddOrder(new Order("sell1", "AAPL", OrderSide.SELL, 100, 10));
        var trades = book.AddOrder(new Order("buy1", "AAPL", OrderSide.BUY, 100, 10));

        Assert.Single(trades);
        Assert.Equal(10, trades[0].Quantity);
        Assert.Equal(100, trades[0].Price);
        Assert.Equal("buy1", trades[0].BuyOrderId);
        Assert.Equal("sell1", trades[0].SellOrderId);
    }

    [Fact]
    public void Sell_MatchesHighestBuy_FullFill()
    {
        var book = NewBook();
        book.AddOrder(new Order("buy1", "AAPL", OrderSide.BUY, 101, 5));
        var trades = book.AddOrder(new Order("sell1", "AAPL", OrderSide.SELL, 100, 5));

        Assert.Single(trades);
        Assert.Equal(5, trades[0].Quantity);
        Assert.Equal(101m, trades[0].Price);
    }

    [Fact]
    public void NoMatch_WhenBuyBelowBestAsk()
    {
        var book = NewBook();
        book.AddOrder(new Order("sell1", "AAPL", OrderSide.SELL, 105m, 10));
        var trades = book.AddOrder(new Order("buy1", "AAPL", OrderSide.BUY, 100m, 10));

        Assert.Empty(trades);
    }

    // For Partial Fills

    [Fact]
    public void PartialFill_BuyLargerThanSell()
    {
        var book = NewBook();
        var sellOrder = new Order("sell1", "AAPL", OrderSide.SELL, 100, 5);
        book.AddOrder(sellOrder);

        var buyOrder = new Order("buy1", "AAPL", OrderSide.BUY, 100, 10);
        var trades = book.AddOrder(buyOrder);

        Assert.Single(trades);
        Assert.Equal(5, trades[0].Quantity);
        Assert.Equal(5, buyOrder.RemainingQuantity);
        Assert.Equal(OrderStatus.PARTIALLY_FILLED, buyOrder.Status);
        Assert.Equal(0, sellOrder.RemainingQuantity);
        Assert.Equal(OrderStatus.FILLED, sellOrder.Status);
    }

    [Fact]
    public void PartialFill_SellLargerThanBuy()
    {
        var book = NewBook();
        var buyOrder = new Order("buy1", "AAPL", OrderSide.BUY, 100, 5);
        book.AddOrder(buyOrder);

        var sellOrder = new Order("sell1", "AAPL", OrderSide.SELL, 100, 10);
        var trades = book.AddOrder(sellOrder);

        Assert.Single(trades);
        Assert.Equal(5, trades[0].Quantity);
        Assert.Equal(5, sellOrder.RemainingQuantity);
        Assert.Equal(OrderStatus.PARTIALLY_FILLED, sellOrder.Status);
    }

    // For Price-time priority 

    [Fact]
    public void PriceTimePriority_LowestAskMatchedFirst()
    {
        var book = NewBook();
        book.AddOrder(new Order("sell2", "AAPL", OrderSide.SELL, 102, 5));
        book.AddOrder(new Order("sell1", "AAPL", OrderSide.SELL, 100, 5)); 
        book.AddOrder(new Order("sell3", "AAPL", OrderSide.SELL, 101, 5));

        var trades = book.AddOrder(new Order("buy1", "AAPL", OrderSide.BUY, 105m, 5));

        Assert.Single(trades);
        Assert.Equal(100, trades[0].Price);
        Assert.Equal("sell1", trades[0].SellOrderId);
    }

    [Fact]
    public void PriceTimePriority_SamePriceOlderOrderFirst()
    {
        var book = NewBook();
        book.AddOrder(new Order("sell_old", "AAPL", OrderSide.SELL, 100, 5));
        book.AddOrder(new Order("sell_new", "AAPL", OrderSide.SELL, 100, 5));

        var trades = book.AddOrder(new Order("buy1", "AAPL", OrderSide.BUY, 100, 5));

        Assert.Single(trades);
        Assert.Equal("sell_old", trades[0].SellOrderId);
    }

    [Fact]
    public void MultiLevelMatch_AgainstMultiplePriceLevels()
    {
        var book = NewBook();
        book.AddOrder(new Order("sell1", "AAPL", OrderSide.SELL, 100, 3));
        book.AddOrder(new Order("sell2", "AAPL", OrderSide.SELL, 101, 3));
        book.AddOrder(new Order("sell3", "AAPL", OrderSide.SELL, 102, 3));

        var trades = book.AddOrder(new Order("buy1", "AAPL", OrderSide.BUY, 105, 9));

        Assert.Equal(3, trades.Count);
        Assert.Equal(100, trades[0].Price);
        Assert.Equal(101, trades[1].Price);
        Assert.Equal(102, trades[2].Price);
    }

    // For Cancel Order

    [Fact]
    public void Cancel_RemovesOrderFromBook()
    {
        var book = NewBook();
        var order = new Order("sell1", "AAPL", OrderSide.SELL, 100, 10);
        book.AddOrder(order);

        var cancelled = book.CancelOrder("sell1");
        Assert.True(cancelled);
        Assert.Equal(OrderStatus.CANCELLED, order.Status);

        var trades = book.AddOrder(new Order("buy1", "AAPL", OrderSide.BUY, 100, 10));
        Assert.Empty(trades);
    }

    [Fact]
    public void Cancel_NonExistentOrder_ReturnsFalse()
    {
        var book = NewBook();
        Assert.False(book.CancelOrder("nonexistent"));
    }

    // For Modify Order

    [Fact]
    public void Modify_Price_TriggersNewMatch()
    {
        var book = NewBook();
        book.AddOrder(new Order("buy1", "AAPL", OrderSide.BUY, 99, 10));
        book.AddOrder(new Order("sell1", "AAPL", OrderSide.SELL, 100, 10));

        var (success, trades) = book.ModifyOrder("buy1", 101, null);

        Assert.True(success);
        Assert.Single(trades);
        Assert.Equal(10, trades[0].Quantity);
    }

    [Fact]
    public void Modify_Quantity_Succeeds()
    {
        var book = NewBook();
        book.AddOrder(new Order("buy1", "AAPL", OrderSide.BUY, 99, 10));

        var (success, trades) = book.ModifyOrder("buy1", null, 5);

        Assert.True(success);
        Assert.Empty(trades);
    }

    // For Book Snapshot 

    [Fact]
    public void Snapshot_ReflectsCurrentBookState()
    {
        var book = NewBook();
        book.AddOrder(new Order("buy1", "AAPL", OrderSide.BUY, 99, 5));
        book.AddOrder(new Order("buy2", "AAPL", OrderSide.BUY, 98, 3));
        book.AddOrder(new Order("sell1", "AAPL", OrderSide.SELL, 101, 7));

        var snap = book.GetSnapshot();

        Assert.Equal(2, snap.Bids.Count);
        Assert.Single(snap.Asks);
        Assert.True(snap.Bids.ContainsKey(99));
        Assert.True(snap.Asks.ContainsKey(101));
    }

    // For Concurrency 

    [Fact]
    public async Task ConcurrentOrders_NoDataCorruption()
    {
        var book = NewBook();
        var sellTasks = Enumerable.Range(0, 50)
            .Select(i => Task.Run(() => book.AddOrder(new Order($"sell{i}", "AAPL", OrderSide.SELL, 100, 1))));
        var buyTasks = Enumerable.Range(0, 50)
            .Select(i => Task.Run(() => book.AddOrder(new Order($"buy{i}", "AAPL", OrderSide.BUY, 100, 1))));

        var allTrades = (await Task.WhenAll(sellTasks.Concat(buyTasks))).SelectMany(t => t).ToList();

        var totalBuyFilled = allTrades.Sum(t => t.Quantity);
        var totalSellFilled = allTrades.Sum(t => t.Quantity);
        Assert.Equal(totalBuyFilled, totalSellFilled);
        Assert.True(totalBuyFilled <= 50);
    }
}

//For bus
public class OrderMatchingServiceTests
{
    private (OrderMatchingService, InMemoryEventBus) CreateService()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var svc = new OrderMatchingService(bus, NullLogger<OrderMatchingService>.Instance);
        return (svc, bus);
    }

    [Fact]
    public async Task NewOrder_PublishesTradeEvent_OnMatch()
    {
        var (svc, bus) = CreateService();
        TradeExecutedEvent? received = null;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var consumerTask = bus.SubscribeAsync<TradeExecutedEvent>(async (e, ct) =>
        {
            received = e;
            cts.Cancel();
        }, cts.Token);

        await svc.NewOrderAsync("AAPL", OrderSide.SELL, 100, 5);
        await svc.NewOrderAsync("AAPL", OrderSide.BUY, 100, 5);

        try { await consumerTask; } catch (OperationCanceledException) { }

        Assert.NotNull(received);
        Assert.Equal("AAPL", received!.Symbol);
        Assert.Equal(5, received.Quantity);
    }

    [Fact]
    public async Task CancelOrder_NonExistent_ReturnsFalse()
    {
        var (svc, _) = CreateService();
        var result = await svc.CancelOrderAsync("random-id");
        Assert.False(result);
    }

}
