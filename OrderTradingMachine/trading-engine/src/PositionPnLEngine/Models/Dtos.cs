using System.ComponentModel.DataAnnotations;

namespace PositionPnLEngine.Models;

public class FillRequest
{
    [Required] public string Symbol { get; set; } = string.Empty;
    [Required] public string Side { get; set; } = string.Empty;  // "BUY" | "SELL"
    [Range(1, int.MaxValue)] public int Quantity { get; set; }
    [Range(0.0001, double.MaxValue)] public decimal Price { get; set; }
    public string? OrderId { get; set; }
}

public class PriceUpdateRequest
{
    [Required] public string Symbol { get; set; } = string.Empty;
    [Range(0.0001, double.MaxValue)] public decimal Price { get; set; }
}
