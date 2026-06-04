using System.Text.Json;

namespace TechNormBlazor.Data.Models;

public class TechRoute
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int Version { get; set; } = 1;
    public string Status { get; set; } = "draft";
    public string Name { get; set; } = null!;
    public JsonDocument? ProcessTree { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int?  SourceEventCount { get; set; }
    public bool  IsAutoUpdate     { get; set; } = false;

    public decimal? LastPci              { get; set; }
    public string?  LastPciStatus        { get; set; }
    public DateTime? LastPciCalculatedAt { get; set; }
    public string?  LastPciSummary       { get; set; }

    public Product Product { get; set; } = null!;
    public User? Creator { get; set; }
    public ICollection<RouteStep> Steps { get; set; } = [];
    public ICollection<CalculationHistory> CalculationHistories { get; set; } = [];
}
