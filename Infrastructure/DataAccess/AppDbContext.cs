using Microsoft.EntityFrameworkCore;
using Domain.Models;

namespace Infrastructure.DataAccess;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Event> Events => Set<Event>();

    public DbSet<Booking> Bookings => Set<Booking>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        UpdateVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        UpdateVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void UpdateVersions()
    {
        foreach (var entry in ChangeTracker.Entries()
                     .Where(entry => entry.Entity is Event or Booking &&
                                     entry.State is EntityState.Added or EntityState.Modified))
        {
            entry.Property("Version").CurrentValue = Guid.NewGuid();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
