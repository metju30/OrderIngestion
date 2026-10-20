using Microsoft.EntityFrameworkCore;
using OrderIngestion.Domain.Entities;

namespace OrderIngestion.Infrastructure.Persistence;

public class OrderDbContext : DbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<IngestionLog> IngestionLogs => Set<IngestionLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(e =>
        {
            e.HasKey(o => o.Id); // primary key
            e.Property(o => o.Id).ValueGeneratedOnAdd();   // SQLite -> INTEGER PRIMARY KEY (auto-increment)
            e.Property(o => o.ExternalId).IsRequired();
            e.HasIndex(o => o.ExternalId).IsUnique();      // <— unique index
            e.Property(o => o.Title).HasMaxLength(500);
            e.Property(o => o.Body).HasMaxLength(4000);
        });

        modelBuilder.Entity<IngestionLog>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.RunKind).HasConversion<string>().IsRequired();
            e.Property(l => l.RunId).IsRequired().HasMaxLength(32);
            e.Property(l => l.Status).HasConversion<string>().IsRequired();
            e.Property(l => l.ErrorMessage).HasMaxLength(2000);
            e.HasIndex(l => l.RunId);
            e.HasIndex(l => l.StartedAtUtc);
        });
    }
}