using System.Text.Json;

namespace TechNormBlazor.Data.Models;

public class RouteStep
{
    public int Id { get; set; }
    public int RouteId { get; set; }
    public int SequenceNum { get; set; }
    public int? OperationId { get; set; }
    public int? ParentStepId { get; set; }
    public string? Description { get; set; }
    public JsonDocument? Parameters { get; set; }

    public TechRoute Route { get; set; } = null!;
    public Operation? Operation { get; set; }
    public RouteStep? ParentStep { get; set; }
    public ICollection<RouteStep> ChildSteps { get; set; } = [];
    public ICollection<TimeNorm> TimeNorms { get; set; } = [];
    public ICollection<MaterialNorm> MaterialNorms { get; set; } = [];
}
