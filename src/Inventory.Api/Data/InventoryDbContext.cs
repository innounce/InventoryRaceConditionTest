using Inventory.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

public class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Product");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(p => p.Sku).HasMaxLength(50).IsRequired();
            entity.HasIndex(p => p.Sku).IsUnique();
            entity.Property(p => p.Name).HasMaxLength(200).IsRequired();
            // Version is the optimistic-lock token. EF Core will append
            // AND "Version" = @old_version to every UPDATE, so concurrent writers
            // that read the same version will get DbUpdateConcurrencyException
            // (0 rows affected) and be converted to HTTP 409 by the service layer.
            entity.Property(p => p.Version).HasDefaultValue(0).IsConcurrencyToken();
        });

        modelBuilder.Entity<InventoryTransaction>(entity =>
        {
            entity.ToTable("InventoryTransaction");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(t => t.ChangeType)
                .HasConversion(v => v == ChangeType.In ? "IN" : "OUT",
                               v => v == "IN" ? ChangeType.In : ChangeType.Out)
                .HasMaxLength(3)
                .IsRequired();
            entity.HasOne<Product>()
                .WithMany()
                .HasForeignKey(t => t.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(t => new { t.ProductId, t.CreatedAt });
        });
    }
}
