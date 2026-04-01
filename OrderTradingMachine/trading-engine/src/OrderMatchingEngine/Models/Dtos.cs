using System.ComponentModel.DataAnnotations;

namespace OrderMatchingEngine.Models;

public class NewOrderRequest
{
    [Required] public string Symbol { get; set; } = string.Empty;
    [Required] public string Side { get; set; } = string.Empty;  // "BUY" | "SELL"
    [Range(0.0001, double.MaxValue)] public decimal Price { get; set; }
    [Range(1, int.MaxValue)] public int Quantity { get; set; }
}

public class ModifyOrderRequest
{
    [Range(0.0001, double.MaxValue)] public decimal? NewPrice { get; set; }
    [Range(1, int.MaxValue)] public int? NewQuantity { get; set; }
}

public class NewOrderResponse
{
    public string OrderId { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public int RemainingQuantity { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<TradeDto> Trades { get; set; } = [];
}

public class ModifyOrderResponse
{
    public string OrderId { get; set; } = string.Empty;
    public List<TradeDto> Trades { get; set; } = [];
}

public class TradeDto
{
    public string TradeId { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public DateTime ExecutedAt { get; set; }
}
