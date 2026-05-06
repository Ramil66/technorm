namespace TechNorm.Api.Data.Entities;

public class Product
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Type { get; set; } = null!; // product | semi_finished | material
    public string? Description { get; set; }

    public ICollection<TechRoute> TechRoutes { get; set; } = [];
}
