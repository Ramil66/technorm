namespace TechNorm.Api.Data.Entities;

public class TimeNorm
{
    public int Id { get; set; }
    public int RouteStepId { get; set; }
    public int? ResourceId { get; set; }
    public decimal NormValue { get; set; }
    public bool IsManual { get; set; } = false;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public RouteStep RouteStep { get; set; } = null!;
    public Resource? Resource { get; set; }
}
