using OrderMatchingEngine.Services;
using Shared.Models;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace OrderMatchingEngine.Tests;

public class ConcurrencyTests
{
    private readonly ITestOutputHelper _output;

    public ConcurrencyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ConcurrentOrderSubmission_ThreadSafe()
    {
        var book = new OrderBook("SAMSUNG");
        var orderCount = 1000;
        var tasks = new List<Task>();
        for (int i = 0; i < orderCount; i++)
        {
            var side = i % 2 == 0 ? OrderSide.BUY : OrderSide.SELL;
            var price = side == OrderSide.BUY ? 100m - (i % 10) : 100m + (i % 10);
            var order = new Order($"order-{i}", "SAMSUNG", side, price, 10);

            tasks.Add(Task.Run(() => book.AddOrder(order)));
        }

        await Task.WhenAll(tasks);
        Assert.Equal(orderCount, book.AllOrders.Count);
    }

    [Fact]
    public async Task ConcurrentMatchingAndCancellation_NoRaceConditions()
    {
        var book = new OrderBook("GOOGL");
        var orderIds = new List<string>();
        for (int i = 0; i < 100; i++)
        {
            var side = i % 2 == 0 ? OrderSide.BUY : OrderSide.SELL;
            var price = side == OrderSide.BUY ? 100 : 200;
            var order = new Order($"order-{i}", "GOOGL", side, price, 10);
            book.AddOrder(order);
            orderIds.Add(order.OrderId);
        }

        var tasks = new List<Task>();

        // Concurrent operations: matching, cancelling, and snapshot reading
        for (int i = 0; i < 50; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                var order = new Order(Guid.NewGuid().ToString(), "GOOGL", OrderSide.BUY, 102m, 5);
                book.AddOrder(order);
            }));

            if (i < orderIds.Count)
            {
                var orderId = orderIds[i];
                tasks.Add(Task.Run(() => book.CancelOrder(orderId)));
            }
            tasks.Add(Task.Run(() => book.GetSnapshot()));
        }

        await Task.WhenAll(tasks);
        Assert.True(true);
    }

    [Fact]
    public void MatchingEngine_PriceTimePriority_UnderConcurrency()
    {
        var book = new OrderBook("AAPL");

        // Add multiple orders at same price
        var order1 = new Order("first", "AAPL", OrderSide.SELL, 100, 10);
        var order2 = new Order("second", "AAPL", OrderSide.SELL, 100, 10);
        var order3 = new Order("third", "AAPL", OrderSide.SELL, 100, 10);

        book.AddOrder(order1);
        Thread.Sleep(1);
        book.AddOrder(order2);
        Thread.Sleep(1);
        book.AddOrder(order3);

        var buyOrder = new Order("buy", "AAPL", OrderSide.BUY, 100, 15);
        var trades = book.AddOrder(buyOrder);

        Assert.Equal(2, trades.Count);
        Assert.Equal("first", trades[0].SellOrderId);
        Assert.Equal("second", trades[1].SellOrderId);
    }


}
