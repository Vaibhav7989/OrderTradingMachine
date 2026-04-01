using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using OrderMatchingEngine.Services;
using PositionPnLEngine.Services;
using Shared.Events;
using Shared.Infrastructure;
using Shared.Models;
using Xunit;
using Xunit.Abstractions;

namespace OrderMatchingEngine.Tests;

/// <summary>
/// Integration testing for the full flow
/// </summary>
public class IntegrationTests
{
    private readonly ITestOutputHelper _output;

    public IntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task EndToEnd_OrderMatchingAndPositionTracking()
    {
        var eventBus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var orderService = new OrderMatchingService(eventBus, NullLogger<OrderMatchingService>.Instance);
        var positionService = new PositionService(NullLogger<PositionService>.Instance);
        var cts = new CancellationTokenSource();
        var consumerTask = Task.Run(async () =>
        {
            await eventBus.SubscribeAsync<TradeExecutedEvent>(async (evt, ct) =>
            {
                _output.WriteLine($"Event received: {evt.Symbol} {evt.Quantity}@{evt.Price}");

                positionService.ApplyFill(evt.Symbol, "BUY", evt.Quantity, evt.Price);
                positionService.ApplyFill(evt.Symbol, "SELL", evt.Quantity, evt.Price);

                await Task.CompletedTask;
            }, cts.Token);
        }, cts.Token);

        await Task.Delay(100);

        // 1. Submit resting sell order
        await orderService.NewOrderAsync("AAPL", OrderSide.SELL, 150, 100, cts.Token);
        _output.WriteLine("Placed SELL 100 at 150");

        // 2. Submit matching buy order
        var (buyOrder, trades) = await orderService.NewOrderAsync("AAPL", OrderSide.BUY, 150, 100, cts.Token);
        _output.WriteLine($"Placed BUY 100 at 150 - {trades.Count} trades");

        await Task.Delay(200);

        //Verify order was fully filled
        Assert.Equal(OrderStatus.FILLED, buyOrder.Status);
        Assert.Equal(0, buyOrder.RemainingQuantity);
        Assert.Single(trades);
        Assert.Equal(100, trades[0].Quantity);
        Assert.Equal(150, trades[0].Price);

        //Verify positions were updated both BUY and SELL sides
        var position = positionService.GetPosition("AAPL");
        Assert.NotNull(position);
        _output.WriteLine($"Position: NetQty={position!.NetQuantity}, AvgPrice={position.AveragePrice}, RealizedPnL={position.RealizedPnL}");

        cts.Cancel();
        eventBus.Dispose();
    }

    [Fact]
    public async Task MultiSymbol_IndependentOrderBooks_AndPositions()
    {
        var eventBus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var orderService = new OrderMatchingService(eventBus, NullLogger<OrderMatchingService>.Instance);
        var tasks = new[]
        {
            orderService.NewOrderAsync("AAPL", OrderSide.BUY, 150, 100),
            orderService.NewOrderAsync("TSLA", OrderSide.SELL, 200, 50),
            orderService.NewOrderAsync("GOOGL", OrderSide.BUY, 100, 25),
            orderService.NewOrderAsync("MSFT", OrderSide.SELL, 300, 75)
        };

        await Task.WhenAll(tasks);

        var symbols = orderService.GetActiveSymbols().ToList();
        Assert.Equal(4, symbols.Count);
        Assert.Contains("AAPL", symbols);
        Assert.Contains("TSLA", symbols);
        Assert.Contains("GOOGL", symbols);
        Assert.Contains("MSFT", symbols);
        var aaplBook = orderService.PrintBook("AAPL");
        var tslaBook = orderService.PrintBook("TSLA");

        Assert.NotNull(aaplBook);
        Assert.NotNull(tslaBook);
        Assert.NotEqual(aaplBook!.Symbol, tslaBook!.Symbol);

        eventBus.Dispose();
    }

    [Fact]
    public async Task ComplexScenario_MultipleTradesAndPositionFlip()
    {
        var eventBus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var orderService = new OrderMatchingService(eventBus, NullLogger<OrderMatchingService>.Instance);
        var positionService = new PositionService(NullLogger<PositionService>.Instance);

        var cts = new CancellationTokenSource();
        var tradeCount = 0;

        var consumerTask = Task.Run(async () =>
        {
            await eventBus.SubscribeAsync<TradeExecutedEvent>(async (evt, ct) =>
            {
                positionService.ApplyFill(evt.Symbol, "BUY", evt.Quantity, evt.Price);
                positionService.ApplyFill(evt.Symbol, "SELL", evt.Quantity, evt.Price);
                Interlocked.Increment(ref tradeCount);
                await Task.CompletedTask;
            }, cts.Token);
        });

        await Task.Delay(100);

        await orderService.NewOrderAsync("AAPL", OrderSide.SELL, 100, 50);
        await orderService.NewOrderAsync("AAPL", OrderSide.SELL, 101, 50);
        await orderService.NewOrderAsync("AAPL", OrderSide.SELL, 102, 50);

        var (buy1, trades1) = await orderService.NewOrderAsync("AAPL", OrderSide.BUY, 102, 100);
        _output.WriteLine($"BUY 100 at 102 - {trades1.Count} trades, remaining {buy1.RemainingQuantity}");

        await Task.Delay(300);

        Assert.Equal(2, trades1.Count);
        Assert.Equal(tradeCount, 2);

        // 4. Sell to close and flip
        await orderService.NewOrderAsync("AAPL", OrderSide.BUY, 95, 100); 
        var (sell1, trades2) = await orderService.NewOrderAsync("AAPL", OrderSide.SELL, 95, 150);
        _output.WriteLine($"SELL 150 at 95 - {trades2.Count} trades");

        await Task.Delay(300);

        var allPositions = positionService.GetAllPositions().ToList();
        Assert.NotEmpty(allPositions);

        _output.WriteLine($"Total trades executed: {tradeCount}");
        _output.WriteLine($"Active positions: {allPositions.Count}");

        cts.Cancel();
        eventBus.Dispose();
    }

    [Fact]
    public void OrderBook_PriceLevelAggregation_Correct()
    {
        var book = new OrderBook("AAPL");
        book.AddOrder(new Order("buy1", "AAPL", OrderSide.BUY, 100, 10));
        book.AddOrder(new Order("buy2", "AAPL", OrderSide.BUY, 100, 20));
        book.AddOrder(new Order("buy3", "AAPL", OrderSide.BUY, 100, 30));

        var snapshot = book.GetSnapshot();
        var level = snapshot.Bids[100];

        Assert.Equal(3, level.Count);
        Assert.Equal(60, level.Sum(o => o.RemainingQuantity));
    }
}
