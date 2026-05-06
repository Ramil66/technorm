namespace TechNormBlazor.Data.Models;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string? Email { get; set; }
    public string Role { get; set; } = null!; // admin | technologist
    public bool IsActive { get; set; } = true;
    public DateTime? LastLogin { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AuditLog> AuditLogs { get; set; } = [];
    public ICollection<Document> Documents { get; set; } = [];
    public ICollection<TechRoute> TechRoutes { get; set; } = [];
}
