using HealthManager.Application;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HealthManager.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ITenantProvider? tenantProvider = null)
    : DbContext(options), IApplicationDbContext
{
    private Guid? TenantClinicId => tenantProvider?.ClinicId;
    private bool BypassTenantFilter => tenantProvider?.IsPlatformAdmin ?? false;

    public DbSet<Clinic> Clinics => Set<Clinic>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<Receivable> Receivables => Set<Receivable>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<WhatsAppMessage> WhatsAppMessages => Set<WhatsAppMessage>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<PatientDocument> PatientDocuments => Set<PatientDocument>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Clinic>().HasIndex(x => x.Slug).IsUnique();
        modelBuilder.Entity<User>().HasIndex(x => x.Email).IsUnique().HasFilter("[DeletedAt] IS NULL");
        modelBuilder.Entity<Patient>().HasIndex(x => new { x.ClinicId, x.Cpf }).IsUnique();
        modelBuilder.Entity<Doctor>().HasIndex(x => new { x.ClinicId, x.Crm }).IsUnique();

        modelBuilder.Entity<User>()
            .HasOne(x => x.Clinic)
            .WithMany(x => x.Users)
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Appointment>()
            .HasOne(x => x.Patient)
            .WithMany()
            .HasForeignKey(x => x.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Appointment>()
            .HasOne(x => x.Doctor)
            .WithMany()
            .HasForeignKey(x => x.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Receivable>()
            .HasOne(x => x.Appointment)
            .WithMany()
            .HasForeignKey(x => x.AppointmentId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Payment>()
            .HasOne(x => x.Receivable)
            .WithMany(x => x.Payments)
            .HasForeignKey(x => x.ReceivableId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PatientDocument>()
            .HasOne(x => x.Patient)
            .WithMany(x => x.Documents)
            .HasForeignKey(x => x.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<RefreshToken>()
            .HasOne(x => x.User)
            .WithMany(x => x.RefreshTokens)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Clinic>().HasQueryFilter(x => x.DeletedAt == null && (BypassTenantFilter || TenantClinicId == null || x.Id == TenantClinicId));
        modelBuilder.Entity<User>().HasQueryFilter(x => x.DeletedAt == null && (BypassTenantFilter || TenantClinicId == null || x.ClinicId == TenantClinicId));
        modelBuilder.Entity<Patient>().HasQueryFilter(x => x.DeletedAt == null && (BypassTenantFilter || TenantClinicId == null || x.ClinicId == TenantClinicId));
        modelBuilder.Entity<Doctor>().HasQueryFilter(x => x.DeletedAt == null && (BypassTenantFilter || TenantClinicId == null || x.ClinicId == TenantClinicId));
        modelBuilder.Entity<Appointment>().HasQueryFilter(x => x.DeletedAt == null && (BypassTenantFilter || TenantClinicId == null || x.ClinicId == TenantClinicId));
        modelBuilder.Entity<Receivable>().HasQueryFilter(x => x.DeletedAt == null && (BypassTenantFilter || TenantClinicId == null || x.ClinicId == TenantClinicId));
        modelBuilder.Entity<Payment>().HasQueryFilter(x => x.DeletedAt == null && (BypassTenantFilter || TenantClinicId == null || x.ClinicId == TenantClinicId));
        modelBuilder.Entity<WhatsAppMessage>().HasQueryFilter(x => x.DeletedAt == null && (BypassTenantFilter || TenantClinicId == null || x.ClinicId == TenantClinicId));
        modelBuilder.Entity<AuditLog>().HasQueryFilter(x => x.DeletedAt == null && (BypassTenantFilter || TenantClinicId == null || x.ClinicId == TenantClinicId));
        modelBuilder.Entity<PatientDocument>().HasQueryFilter(x => x.DeletedAt == null && (BypassTenantFilter || TenantClinicId == null || x.ClinicId == TenantClinicId));
        modelBuilder.Entity<RefreshToken>().HasQueryFilter(x => x.DeletedAt == null && (BypassTenantFilter || TenantClinicId == null || x.ClinicId == TenantClinicId));
        modelBuilder.Entity<WebhookEvent>().HasQueryFilter(x => x.DeletedAt == null && (BypassTenantFilter || TenantClinicId == null || x.ClinicId == TenantClinicId));
        modelBuilder.Entity<OutboxEvent>().HasQueryFilter(x => x.DeletedAt == null);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entries = ChangeTracker.Entries<Entity>();
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=healthmanager;Username=postgres;Password=postgres");
        return new AppDbContext(optionsBuilder.Options);
    }
}
