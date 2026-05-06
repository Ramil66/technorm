namespace TechNorm.Api.Data.Entities;

public class Operation
{
    public int Id { get; set; }
    public string? Code { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    public ICollection<RouteStep> RouteSteps { get; set; } = [];
}
