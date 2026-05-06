namespace TechNormBlazor.Data.Models;

public class Material
{
    public int Id { get; set; }
    public string? Code { get; set; }
    public string Name { get; set; } = null!;
    public string? Unit { get; set; } // VARCHAR(30), e.g. "кг", "шт", "м"
    public string? Description { get; set; }

    public ICollection<MaterialNorm> MaterialNorms { get; set; } = [];
}
