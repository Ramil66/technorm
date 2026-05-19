namespace TechNormBlazor.Data.Models;

public class OperationNameMapping
{
    public int       Id             { get; set; }
    public string    RawName        { get; set; } = "";
    public string    NormalizedName { get; set; } = "";
    public int       OperationId    { get; set; }
    public decimal   Confidence     { get; set; }
    public bool      IsConfirmed    { get; set; }
    public int?      ConfirmedBy    { get; set; }
    public DateTime? ConfirmedAt    { get; set; }
    public DateTime  CreatedAt      { get; set; } = DateTime.UtcNow;

    public Operation Operation       { get; set; } = null!;
    public User?     ConfirmedByUser { get; set; }
}
