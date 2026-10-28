using AppointmentBooking.DomainLayer.Common;
using AppointmentBooking.DomainLayer.Entities;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace AppointmentBooking.InfrastructureLayer.Data;

public class BookingDbContext : DbContext
{
    public BookingDbContext(DbContextOptions<BookingDbContext> options) : base(options)
    {
    }

    // Core Multi-Tenant Tables
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // Service Provider & Catalog Tables (Module 1 & 2)
    public DbSet<ServiceProvider> ServiceProviders => Set<ServiceProvider>();
    public DbSet<ProviderAvailability> ProviderAvailabilities => Set<ProviderAvailability>();

    // Appointment Booking Tables (Module 3, 4, 5, 6, 7)
    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. Global Soft Delete Query Filter for all BaseEntity types
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var property = Expression.Property(parameter, nameof(BaseEntity.IsDeleted));
                var falseConstant = Expression.Constant(false);
                var filter = Expression.Lambda(Expression.Equal(property, falseConstant), parameter);
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
            }
        }

        // 2. Tenant Configuration
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.ToTable("Tenants");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Name).IsRequired().HasMaxLength(150);
            entity.Property(t => t.TenantCode).IsRequired().HasMaxLength(50);
            entity.HasIndex(t => t.TenantCode).IsUnique();
            entity.Property(t => t.ContactEmail).HasMaxLength(256);
            entity.Property(t => t.ContactPhone).HasMaxLength(50);
            entity.Property(t => t.Address).HasMaxLength(500);
            entity.Property(t => t.LogoUrl).HasMaxLength(500);
            entity.Property(t => t.Status).IsRequired();
        });

        // 3. User Configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.FirstName).IsRequired().HasMaxLength(75);
            entity.Property(u => u.LastName).IsRequired().HasMaxLength(75);
            entity.Property(u => u.Email).IsRequired().HasMaxLength(256);
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.PhoneNumber).HasMaxLength(50);
            entity.Property(u => u.PasswordHash).IsRequired(false);
            entity.Property(u => u.Role).IsRequired();
            entity.Property(u => u.IsActive).IsRequired().HasDefaultValue(true);
            entity.Property(u => u.ProfilePictureUrl).HasMaxLength(500);
            entity.Property(u => u.Address).HasMaxLength(500);
            entity.Property(u => u.City).HasMaxLength(100);
            entity.Property(u => u.State).HasMaxLength(100);
            entity.Property(u => u.PostalCode).HasMaxLength(20);

            entity.HasOne(u => u.Tenant)
                  .WithMany(t => t.Users)
                  .HasForeignKey(u => u.TenantId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // 4. RefreshToken Configuration
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens");
            entity.HasKey(rt => rt.Id);
            entity.Property(rt => rt.TokenHash).IsRequired().HasMaxLength(500);
            entity.HasIndex(rt => rt.TokenHash).IsUnique();
            entity.Property(rt => rt.CreatedAt).IsRequired();
            entity.Property(rt => rt.ExpiresAt).IsRequired();

            entity.HasOne(rt => rt.User)
                  .WithMany(u => u.RefreshTokens)
                  .HasForeignKey(rt => rt.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Matching query filter: Filter out refresh tokens for soft-deleted users
            entity.HasQueryFilter(rt => !rt.User!.IsDeleted);
        });

        // 5. ServiceProvider Configuration (Module 1 - Zero Redundancy)
        modelBuilder.Entity<ServiceProvider>(entity =>
        {
            entity.ToTable("ServiceProviders");
            entity.HasKey(sp => sp.Id);
            entity.Property(sp => sp.ServiceType).IsRequired().HasMaxLength(100);
            entity.Property(sp => sp.Bio).HasMaxLength(1000);
            entity.Property(sp => sp.ChargesAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
            entity.Property(sp => sp.Status).IsRequired();

            entity.HasOne<Tenant>()
                  .WithMany()
                  .HasForeignKey(sp => sp.TenantId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(sp => sp.User)
                  .WithOne(u => u.ServiceProvider)
                  .HasForeignKey<ServiceProvider>(sp => sp.UserId)
                  .IsRequired()
                  .OnDelete(DeleteBehavior.Restrict);
        });


        // 8. ProviderAvailability Configuration (Module 2)
        modelBuilder.Entity<ProviderAvailability>(entity =>
        {
            entity.ToTable("ProviderAvailabilities");
            entity.HasKey(pa => pa.Id);
            entity.Property(pa => pa.DayOfWeek).IsRequired();
            entity.Property(pa => pa.SpecificDate).IsRequired(false);
            entity.Property(pa => pa.StartTime).IsRequired();
            entity.Property(pa => pa.EndTime).IsRequired();
            entity.Property(pa => pa.SlotDurationMinutes).IsRequired().HasDefaultValue(30);
            entity.Property(pa => pa.BreakStartTime).IsRequired(false);
            entity.Property(pa => pa.BreakEndTime).IsRequired(false);
            entity.Property(pa => pa.BlockedSlots).HasMaxLength(1000).IsRequired(false);
            entity.Property(pa => pa.IsAvailable).IsRequired().HasDefaultValue(true);

            entity.HasOne(pa => pa.ServiceProvider)
                  .WithMany(sp => sp.Availabilities)
                  .HasForeignKey(pa => pa.ServiceProviderId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Tenant>()
                  .WithMany()
                  .HasForeignKey(pa => pa.TenantId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // 9. Appointment Configuration (Module 3, 4, 5, 6, 7)
        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.ToTable("Appointments");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.AppointmentNumber).IsRequired().HasMaxLength(50);
            entity.HasIndex(a => a.AppointmentNumber).IsUnique();

            // High-performance Composite Query Indexes (Dashboard & Filter queries)
            entity.HasIndex(a => new { a.ServiceProviderId, a.AppointmentDate, a.Status });
            entity.HasIndex(a => new { a.TenantId, a.AppointmentDate });
            entity.HasIndex(a => new { a.CustomerId, a.AppointmentDate });

            // Database-Level Concurrency Unique Filtered Index to prevent double booking race conditions
            entity.HasIndex(a => new { a.ServiceProviderId, a.AppointmentDate, a.StartTime })
                  .HasFilter("[Status] <> 3 AND [Status] <> 4 AND [Status] <> 6 AND [IsDeleted] = 0")
                  .IsUnique();

            entity.Property(a => a.Reason).HasMaxLength(500);
            entity.Property(a => a.AppointmentDate).IsRequired();
            entity.Property(a => a.StartTime).IsRequired();
            entity.Property(a => a.EndTime).IsRequired();
            entity.Property(a => a.Status).IsRequired();
            entity.Property(a => a.CancellationReason).HasMaxLength(500);
            entity.Property(a => a.RejectionReason).HasMaxLength(500);
            entity.Property(a => a.RescheduleNotes).HasMaxLength(500);

            // Concurrency token to prevent overlapping appointments
            entity.Property(a => a.RowVersion).IsRowVersion();

            entity.HasOne(a => a.ServiceProvider)
                  .WithMany(sp => sp.Appointments)
                  .HasForeignKey(a => a.ServiceProviderId)
                  .OnDelete(DeleteBehavior.Restrict);


            entity.HasOne(a => a.Customer)
                  .WithMany(u => u.CustomerAppointments)
                  .HasForeignKey(a => a.CustomerId)
                  .IsRequired()
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<Tenant>()
                  .WithMany()
                  .HasForeignKey(a => a.TenantId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(a => a.RescheduledFromAppointment)
                  .WithMany()
                  .HasForeignKey(a => a.RescheduledFromAppointmentId)
                  .OnDelete(DeleteBehavior.Restrict);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateAuditTracking();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        UpdateAuditTracking();
        return base.SaveChanges();
    }

    private void UpdateAuditTracking()
    {
        var entries = ChangeTracker.Entries<BaseEntity>();

        foreach (var entry in entries)
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = DateTime.UtcNow;
                    entry.Entity.IsDeleted = false;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    break;

                case EntityState.Deleted:
                    // Soft delete interception
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.DeletedAt = DateTime.UtcNow;
                    break;
            }
        }
    }
}
