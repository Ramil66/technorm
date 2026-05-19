using Microsoft.EntityFrameworkCore;
using TechNormBlazor.Data.Models;

namespace TechNormBlazor.Data;

public class TechNormDbContext(DbContextOptions<TechNormDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Operation> Operations => Set<Operation>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<TechRoute> TechRoutes => Set<TechRoute>();
    public DbSet<RouteStep> RouteSteps => Set<RouteStep>();
    public DbSet<TimeNorm> TimeNorms => Set<TimeNorm>();
    public DbSet<MaterialNorm> MaterialNorms => Set<MaterialNorm>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<CalculationHistory> CalculationHistories => Set<CalculationHistory>();
    public DbSet<AuditLog>             AuditLogs             => Set<AuditLog>();
    public DbSet<EventLog>             EventLogs             => Set<EventLog>();
    public DbSet<OperationNameMapping> OperationNameMappings => Set<OperationNameMapping>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Username).HasColumnName("username");
            e.Property(x => x.PasswordHash).HasColumnName("password_hash");
            e.Property(x => x.FullName).HasColumnName("full_name");
            e.Property(x => x.Email).HasColumnName("email");
            e.Property(x => x.Role).HasColumnName("role");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.LastLogin).HasColumnName("last_login");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<Operation>(e =>
        {
            e.ToTable("operations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Description).HasColumnName("description");
        });

        modelBuilder.Entity<Resource>(e =>
        {
            e.ToTable("resources");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Type).HasColumnName("type");
            e.Property(x => x.Characteristics).HasColumnName("characteristics").HasColumnType("jsonb");
            e.Property(x => x.IsActive).HasColumnName("is_active");
        });

        modelBuilder.Entity<Material>(e =>
        {
            e.ToTable("materials");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Unit).HasColumnName("unit");
            e.Property(x => x.Description).HasColumnName("description");
        });

        modelBuilder.Entity<Product>(e =>
        {
            e.ToTable("products");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Type).HasColumnName("type");
            e.Property(x => x.Description).HasColumnName("description");
        });

        modelBuilder.Entity<TechRoute>(e =>
        {
            e.ToTable("tech_routes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProductId).HasColumnName("product_id");
            e.Property(x => x.Version).HasColumnName("version");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.ProcessTree).HasColumnName("process_tree").HasColumnType("jsonb");
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.PublishedAt).HasColumnName("published_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne(x => x.Product).WithMany(p => p.TechRoutes).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Creator).WithMany(u => u.TechRoutes).HasForeignKey(x => x.CreatedBy);
        });

        modelBuilder.Entity<RouteStep>(e =>
        {
            e.ToTable("route_steps");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.RouteId).HasColumnName("route_id");
            e.Property(x => x.SequenceNum).HasColumnName("sequence_num");
            e.Property(x => x.OperationId).HasColumnName("operation_id");
            e.Property(x => x.ParentStepId).HasColumnName("parent_step_id");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.Parameters).HasColumnName("parameters").HasColumnType("jsonb");
            e.HasOne(x => x.Route).WithMany(r => r.Steps).HasForeignKey(x => x.RouteId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Operation).WithMany(o => o.RouteSteps).HasForeignKey(x => x.OperationId);
            e.HasOne(x => x.ParentStep).WithMany(s => s.ChildSteps).HasForeignKey(x => x.ParentStepId);
        });

        modelBuilder.Entity<TimeNorm>(e =>
        {
            e.ToTable("time_norms");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.RouteStepId).HasColumnName("route_step_id");
            e.Property(x => x.ResourceId).HasColumnName("resource_id");
            e.Property(x => x.NormValue).HasColumnName("norm_value");
            e.Property(x => x.IsManual).HasColumnName("is_manual");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne(x => x.RouteStep).WithMany(s => s.TimeNorms).HasForeignKey(x => x.RouteStepId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Resource).WithMany(r => r.TimeNorms).HasForeignKey(x => x.ResourceId);
        });

        modelBuilder.Entity<MaterialNorm>(e =>
        {
            e.ToTable("material_norms");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.RouteStepId).HasColumnName("route_step_id");
            e.Property(x => x.MaterialId).HasColumnName("material_id");
            e.Property(x => x.ConsumptionRate).HasColumnName("consumption_rate");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne(x => x.RouteStep).WithMany(s => s.MaterialNorms).HasForeignKey(x => x.RouteStepId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Material).WithMany(m => m.MaterialNorms).HasForeignKey(x => x.MaterialId);
        });

        modelBuilder.Entity<Document>(e =>
        {
            e.ToTable("documents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.OriginalName).HasColumnName("original_name");
            e.Property(x => x.FilePath).HasColumnName("file_path");
            e.Property(x => x.FileType).HasColumnName("file_type");
            e.Property(x => x.FileSize).HasColumnName("file_size");
            e.Property(x => x.Sha256Hash).HasColumnName("sha256_hash");
            e.Property(x => x.UploadedBy).HasColumnName("uploaded_by");
            e.Property(x => x.UploadedAt).HasColumnName("uploaded_at");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.ProcessedAt).HasColumnName("processed_at");
            e.HasOne(x => x.Uploader).WithMany(u => u.Documents).HasForeignKey(x => x.UploadedBy);
        });

        modelBuilder.Entity<CalculationHistory>(e =>
        {
            e.ToTable("calculation_history");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProductId).HasColumnName("product_id");
            e.Property(x => x.RouteId).HasColumnName("route_id");
            e.Property(x => x.CalculatedBy).HasColumnName("calculated_by");
            e.Property(x => x.CalculatedAt).HasColumnName("calculated_at");
            e.Property(x => x.Metrics).HasColumnName("metrics").HasColumnType("jsonb");
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
            e.HasOne(x => x.Route).WithMany(r => r.CalculationHistories).HasForeignKey(x => x.RouteId);
            e.HasOne(x => x.Calculator).WithMany().HasForeignKey(x => x.CalculatedBy);
        });

        modelBuilder.Entity<EventLog>(e =>
        {
            e.ToTable("event_logs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CaseId).HasColumnName("case_id");
            e.Property(x => x.Activity).HasColumnName("activity");
            e.Property(x => x.Timestamp).HasColumnName("timestamp");
            e.Property(x => x.ResourceId).HasColumnName("resource_id");
            e.Property(x => x.ProductId).HasColumnName("product_id");
            e.Property(x => x.Duration).HasColumnName("duration");
            e.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
            e.Property(x => x.Source).HasColumnName("source");
            e.Property(x => x.SourceDocumentId).HasColumnName("source_document_id");
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.Resource).WithMany().HasForeignKey(x => x.ResourceId);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
            e.HasOne(x => x.SourceDocument).WithMany().HasForeignKey(x => x.SourceDocumentId);
            e.HasOne(x => x.Creator).WithMany().HasForeignKey(x => x.CreatedBy);
        });

        modelBuilder.Entity<OperationNameMapping>(e =>
        {
            e.ToTable("operation_name_mappings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.RawName).HasColumnName("raw_name").HasMaxLength(512);
            e.Property(x => x.NormalizedName).HasColumnName("normalized_name").HasMaxLength(512);
            e.Property(x => x.OperationId).HasColumnName("operation_id");
            e.Property(x => x.Confidence).HasColumnName("confidence").HasColumnType("numeric(5,4)");
            e.Property(x => x.IsConfirmed).HasColumnName("is_confirmed");
            e.Property(x => x.ConfirmedBy).HasColumnName("confirmed_by");
            e.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.RawName).IsUnique();
            e.HasOne(x => x.Operation).WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ConfirmedByUser).WithMany().HasForeignKey(x => x.ConfirmedBy);
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_logs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Action).HasColumnName("action");
            e.Property(x => x.EntityType).HasColumnName("entity_type");
            e.Property(x => x.EntityId).HasColumnName("entity_id");
            e.Property(x => x.OldValue).HasColumnName("old_value").HasColumnType("jsonb");
            e.Property(x => x.NewValue).HasColumnName("new_value").HasColumnType("jsonb");
            e.Property(x => x.IpAddress).HasColumnName("ip_address");
            e.Property(x => x.UserAgent).HasColumnName("user_agent");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.User).WithMany(u => u.AuditLogs).HasForeignKey(x => x.UserId);
        });
    }
}
