using Microsoft.EntityFrameworkCore;
using TechNormBlazor.Data;
using TechNormBlazor.Data.Models;

namespace TechNormBlazor.Services;

public interface IDocumentService
{
    Task<List<Document>> GetAllAsync();
    Task<Document?> GetByIdAsync(int id);
    Task<Document> SaveAsync(Stream fileStream, string originalName, string fileType, int uploadedBy);
    Task<Stream> OpenFileAsync(int id);
    Task UpdateStatusAsync(int id, string status, DateTime? processedAt = null);
    Task DeleteAsync(int id);
}

public class DocumentService(
    IDbContextFactory<TechNormDbContext> factory,
    IWebHostEnvironment env) : IDocumentService
{
    private string UploadsRoot => Path.Combine(env.ContentRootPath, "uploads");

    public async Task<List<Document>> GetAllAsync()
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.Documents
            .Include(d => d.Uploader)
            .OrderByDescending(d => d.UploadedAt)
            .ToListAsync();
    }

    public async Task<Document?> GetByIdAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.Documents
            .Include(d => d.Uploader)
            .FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<Document> SaveAsync(Stream fileStream, string originalName, string fileType, int uploadedBy)
    {
        Directory.CreateDirectory(UploadsRoot);

        var storedName = $"{Guid.NewGuid():N}{Path.GetExtension(originalName)}";
        var filePath = Path.Combine(UploadsRoot, storedName);

        await using (var fileOut = File.Create(filePath))
            await fileStream.CopyToAsync(fileOut);

        var size = new FileInfo(filePath).Length;

        string hash;
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        await using (var hashStream = File.OpenRead(filePath))
        {
            var hashBytes = await sha256.ComputeHashAsync(hashStream);
            hash = Convert.ToHexString(hashBytes);
        }

        var doc = new Document
        {
            OriginalName = originalName,
            FilePath = filePath,
            FileType = fileType,
            FileSize = size,
            Sha256Hash = hash,
            UploadedBy = uploadedBy,
            UploadedAt = DateTime.UtcNow,
            Status = "new",
        };

        using var db = await factory.CreateDbContextAsync();
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    public async Task<Stream> OpenFileAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();
        var doc = await db.Documents.FindAsync(id)
            ?? throw new FileNotFoundException($"Документ {id} не найден.");
        if (!File.Exists(doc.FilePath))
            throw new FileNotFoundException($"Файл документа {id} отсутствует на диске.");
        return File.OpenRead(doc.FilePath);
    }

    public async Task UpdateStatusAsync(int id, string status, DateTime? processedAt = null)
    {
        var at = processedAt ?? (status == "processed" ? DateTime.UtcNow : (DateTime?)null);
        using var db = await factory.CreateDbContextAsync();
        await db.Documents.Where(d => d.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, status)
                .SetProperty(d => d.ProcessedAt, at));
    }

    public async Task DeleteAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();
        var doc = await db.Documents.FindAsync(id);
        if (doc is null) return;
        await db.EventLogs.Where(e => e.SourceDocumentId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.SourceDocumentId, (int?)null));
        db.Documents.Remove(doc);
        await db.SaveChangesAsync();
        if (File.Exists(doc.FilePath))
            File.Delete(doc.FilePath);
    }
}
