using System.Text.Json;

namespace TechNormBlazor.Data.Models;

public class Resource
{
    public int Id { get; set; }
    public string? Code { get; set; }
    public string Name { get; set; } = null!;
    public string Type { get; set; } = null!; // equipment | tool | personnel | workstation
    public JsonDocument? Characteristics { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<TimeNorm> TimeNorms { get; set; } = [];
}
