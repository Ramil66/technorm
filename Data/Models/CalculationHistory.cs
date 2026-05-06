using System.Text.Json;

namespace TechNorm.Api.Data.Entities;

public class CalculationHistory
{
    public int Id { get; set; }
    public int? ProductId { get; set; }
    public int? RouteId { get; set; }
    public int? CalculatedBy { get; set; }
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
    public JsonDocument? Metrics { get; set; }

    public Product? Product { get; set; }
    public TechRoute? Route { get; set; }
    public User? Calculator { get; set; }
}
