using Microsoft.Extensions.Logging.Abstractions;
using PositionPnLEngine.Models;
using PositionPnLEngine.Services;
using Xunit;

namespace PositionPnLEngine.Tests;

public class PositionTests
{
    // For Long position Orders

    [Fact]
    public void Buy_EstablishesLongPosition()
    {
        var pos = new Position("AAPL");
        pos.ApplyFill("BUY", 100, 150);

        Assert.Equal(100, pos.NetQuantity);
        Assert.Equal(150, pos.AveragePrice);
        Assert.Equal(0, pos.RealizedPnL);
    }

    [Fact]
    public void Buy_AddToLong_UpdatesAveragePrice()
    {
        var pos = new Position("AAPL");
        pos.ApplyFill("BUY", 100, 100);
        pos.ApplyFill("BUY", 100, 120);

        Assert.Equal(200, pos.NetQuantity);
        Assert.Equal(110, pos.AveragePrice); 
        Assert.Equal(0m, pos.RealizedPnL);
    }

    [Fact]
    public void Sell_ReducesLong_RealizesPnL()
    {
        var pos = new Position("AAPL");
        pos.ApplyFill("BUY", 100, 100);
        pos.ApplyFill("SELL", 50, 120);

        Assert.Equal(50, pos.NetQuantity);
        Assert.Equal(100m, pos.AveragePrice); 
        Assert.Equal(1000m, pos.RealizedPnL);  
    }

    [Fact]
    public void Sell_FullClose_ZerosPosition()
    {
        var pos = new Position("AAPL");
        pos.ApplyFill("BUY", 100, 100);
        pos.ApplyFill("SELL", 100, 110);

        Assert.Equal(0, pos.NetQuantity);
        Assert.Equal(0m, pos.AveragePrice);
        Assert.Equal(1000m, pos.RealizedPnL); 
    }

    [Fact]
    public void Sell_AtLoss_NegativeRealizedPnL()
    {
        var pos = new Position("AAPL");
        pos.ApplyFill("BUY", 100, 150);
        pos.ApplyFill("SELL", 100, 130);

        Assert.Equal(-2000, pos.RealizedPnL);
    }

    // For Position flip long to short

    [Fact]
    public void Sell_FlipsLongToShort()
    {
        var pos = new Position("AAPL");
        pos.ApplyFill("BUY", 50, 100);
        pos.ApplyFill("SELL", 80, 110);

        Assert.Equal(-30, pos.NetQuantity);
        Assert.Equal(110, pos.AveragePrice); 
        Assert.Equal(500, pos.RealizedPnL);  
    }

    // For Short position Orders

    [Fact]
    public void Sell_EstablishesShortPosition()
    {
        var pos = new Position("AAPL");
        pos.ApplyFill("SELL", 100, 200);

        Assert.Equal(-100, pos.NetQuantity);
        Assert.Equal(200, pos.AveragePrice);
        Assert.Equal(0m, pos.RealizedPnL);
    }

    [Fact]
    public void Buy_CoversShort_RealizesPnL()
    {
        var pos = new Position("AAPL");
        pos.ApplyFill("SELL", 1, 200); 
        pos.ApplyFill("BUY", 1, 180);

        Assert.Equal(0, pos.NetQuantity);
        Assert.Equal(0, pos.AveragePrice);
        Assert.Equal(20, pos.RealizedPnL);
    }

    // For Unrealized PnL 

    [Fact]
    public void UnrealizedPnL_LongPosition_PositiveWhenPriceAboveCost()
    {
        var pos = new Position("AAPL");
        pos.ApplyFill("BUY", 100, 100);
        pos.UpdateMarketPrice(120);

        Assert.Equal(2000, pos.UnrealizedPnL); 
        Assert.Equal(2000, pos.TotalPnL);
    }

    [Fact]
    public void UnrealizedPnL_ShortPosition_PositiveWhenPriceFalls()
    {
        var pos = new Position("AAPL");
        pos.ApplyFill("SELL", 100, 200);
        pos.UpdateMarketPrice(180);

        Assert.Equal(2000, pos.UnrealizedPnL);
    }

    [Fact]
    public void UnrealizedPnL_ZeroBeforeMarketPriceSet()
    {
        var pos = new Position("AAPL");
        pos.ApplyFill("BUY", 100, 100);

        Assert.Equal(0m, pos.UnrealizedPnL);
    }

    [Fact]
    public void TotalPnL_SumOfRealizedAndUnrealized()
    {
        var pos = new Position("AAPL");
        pos.ApplyFill("BUY", 200, 100);
        pos.ApplyFill("SELL", 100, 120);
        pos.UpdateMarketPrice(130);

        Assert.Equal(2000, pos.RealizedPnL);
        Assert.Equal(3000, pos.UnrealizedPnL);
        Assert.Equal(5000, pos.TotalPnL);
    }

    // Fr Multiple order fills

    [Fact]
    public void MultiplePartialFills_AveragePrice_Correct()
    {
        var pos = new Position("AAPL");
        pos.ApplyFill("BUY", 10, 100);
        pos.ApplyFill("BUY", 20, 110);
        pos.ApplyFill("BUY", 30, 120);

        var expected = 6800m / 60m;
        Assert.Equal(expected, pos.AveragePrice, 2);
        Assert.Equal(60, pos.NetQuantity);
    }

    // Checking Thread safety 

    [Fact]
    public async Task ConcurrentFills_NoRaceCondition()
    {
        var pos = new Position("AAPL");
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() => pos.ApplyFill("BUY", 1, 100)));

        await Task.WhenAll(tasks);

        Assert.Equal(100, pos.NetQuantity);
        Assert.Equal(100, pos.AveragePrice);
    }
}

//Testing only servie layer

public class PositionServiceTests
{
    private PositionService CreateService() =>
        new(NullLogger<PositionService>.Instance);

    [Fact]
    public void ApplyFill_CreatesPositionIfNotExists()
    {
        var svc = CreateService();
        svc.ApplyFill("AAPL", "BUY", 100, 150);

        var snap = svc.GetPosition("AAPL");
        Assert.NotNull(snap);
        Assert.Equal(100, snap!.NetQuantity);
    }

    [Fact]
    public void GetPosition_CaseInsensitive()
    {
        var svc = CreateService();
        svc.ApplyFill("aapl", "BUY", 10, 100);

        Assert.NotNull(svc.GetPosition("AAPL"));
        Assert.NotNull(svc.GetPosition("aapl"));
    }

    [Fact]
    public void GetPosition_ReturnsNull_ForUnknownSymbol()
    {
        var svc = CreateService();
        Assert.Null(svc.GetPosition("Random"));
    }

    [Fact]
    public void GetAllPositions_ReturnsAll()
    {
        var svc = CreateService();
        svc.ApplyFill("AAPL", "BUY", 10, 100);
        svc.ApplyFill("TSLA", "BUY", 5, 200);
        svc.ApplyFill("GOOGL", "SELL", 3, 150);

        var all = svc.GetAllPositions().ToList();
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void UpdateMarketPrice_AffectsUnrealizedPnL()
    {
        var svc = CreateService();
        svc.ApplyFill("AAPL", "BUY", 100, 100);
        svc.UpdateMarketPrice("AAPL", 110);

        var snap = svc.GetPosition("AAPL");
        Assert.Equal(1000m, snap!.UnrealizedPnL);
    }

    [Fact]
    public void AggregatedPnL_SumsAcrossSymbols()
    {
        var svc = CreateService();

        svc.ApplyFill("AAPL", "BUY", 100, 100);
        svc.ApplyFill("AAPL", "SELL", 100, 120); 

        svc.ApplyFill("TSLA", "BUY", 50, 200);
        svc.UpdateMarketPrice("TSLA", 210);  

        var positions = svc.GetAllPositions().ToList();
        Assert.Equal(2000, positions.Sum(p => p.RealizedPnL));
        Assert.Equal(500, positions.Sum(p => p.UnrealizedPnL));
        Assert.Equal(2500, positions.Sum(p => p.TotalPnL));
    }
}
