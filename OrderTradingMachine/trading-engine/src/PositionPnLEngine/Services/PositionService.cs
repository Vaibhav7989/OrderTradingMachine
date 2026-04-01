using System.Collections.Concurrent;
using PositionPnLEngine.Models;

namespace PositionPnLEngine.Services;

public interface IPositionService
{
    void ApplyFill(string symbol, string side, int quantity, decimal price);
    void UpdateMarketPrice(string symbol, decimal price);
    PositionSnapshot? GetPosition(string symbol);
    IEnumerable<PositionSnapshot> GetAllPositions();
}

public class PositionService : IPositionService
{
    private readonly ConcurrentDictionary<string, Position> _positions = new();
    private readonly ILogger<PositionService> _logger;

    public PositionService(ILogger<PositionService> logger) => _logger = logger;

    public void ApplyFill(string symbol, string side, int quantity, decimal price)
    {
        var position = _positions.GetOrAdd(symbol.ToUpper(), s => new Position(s));
        position.ApplyFill(side, quantity, price);
        _logger.LogInformation("FILL {Symbol} with {Side} - {Qty} at {Price}", symbol, side, quantity, price);
    }

    public void UpdateMarketPrice(string symbol, decimal price)
    {
        var position = _positions.GetOrAdd(symbol.ToUpper(), s => new Position(s));
        position.UpdateMarketPrice(price);
        _logger.LogInformation("PRICE updated for {Symbol} with {Price}", symbol, price);
    }

    public PositionSnapshot? GetPosition(string symbol) =>
        _positions.TryGetValue(symbol.ToUpper(), out var pos) ? pos.GetSnapshot() : null;

    public IEnumerable<PositionSnapshot> GetAllPositions() =>
        _positions.Values.Select(p => p.GetSnapshot());
}
