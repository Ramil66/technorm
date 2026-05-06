using System.Text.Json;

namespace TechNormBlazor.Data.Models;

public class EventLog
{
    public long Id { get; set; }
    public string CaseId { get; set; } = string.Empty;
    public string Activity { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public int? ResourceId { get; set; }
    public int? ProductId { get; set; }
    public TimeSpan? Duration { get; set; }
    public JsonDocument? Metadata { get; set; }
    public string Source { get; set; } = "manual"; // manual | document
    public int? SourceDocumentId { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Resource? Resource { get; set; }
    public Product? Product { get; set; }
    public Document? SourceDocument { get; set; }
    public User? Creator { get; set; }
}
