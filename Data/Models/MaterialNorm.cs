namespace TechNorm.Api.Data.Entities;

public class MaterialNorm
{
    public int Id { get; set; }
    public int RouteStepId { get; set; }
    public int MaterialId { get; set; }
    public decimal ConsumptionRate { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public RouteStep RouteStep { get; set; } = null!;
    public Material Material { get; set; } = null!;
}
