namespace TechNormBlazor.Data.Models;

public class Document
{
    public int Id { get; set; }
    public string OriginalName { get; set; } = null!;
    public string FilePath { get; set; } = null!;
    public string FileType { get; set; } = null!; // excel | pdf | scan | image
    public long? FileSize { get; set; }
    public string? Sha256Hash { get; set; }
    public int? UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "new"; // new | processing | processed | error
    public DateTime? ProcessedAt { get; set; }

    public User? Uploader { get; set; }
    public ICollection<CalculationHistory> CalculationHistories { get; set; } = [];
}
