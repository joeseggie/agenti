using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using EastSeat.Agenti.Shared.Domain.Entities;

namespace EastSeat.Agenti.Web.Data;

/// <summary>
/// Main database context for the Agenti ERP system.
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    // Domain entities
    public DbSet<WalletType> WalletTypes { get; set; }
    public DbSet<Wallet> Wallets { get; set; }
    public DbSet<CashSession> CashSessions { get; set; }
    public DbSet<CashCount> CashCounts { get; set; }
    public DbSet<CashCountDetail> CashCountDetails { get; set; }
    public DbSet<Transaction> Transactions { get; set; }
    public DbSet<Discrepancy> Discrepancies { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Configure WalletType
        builder.Entity<WalletType>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.Type)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(50);
            entity.HasIndex(e => e.Type);
        });

        // Configure Wallet
        builder.Entity<Wallet>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Currency).IsRequired().HasMaxLength(3);
            entity.Property(e => e.Balance).HasPrecision(18, 2);
            entity.HasOne(e => e.WalletType)
                .WithMany(w => w.Wallets)
                .HasForeignKey(e => e.WalletTypeId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.AgentId, e.BranchId });
        });

        // Configure CashSession
        builder.Entity<CashSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SessionDate).IsRequired();
            entity.Property(e => e.Status)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(50);
            entity.HasIndex(e => new { e.AgentId, e.SessionDate }).IsUnique();
            entity.HasIndex(e => new { e.Status, e.BranchId });
        });

        // Configure CashCount
        builder.Entity<CashCount>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TotalAmount).HasPrecision(18, 2);
            entity.HasOne(e => e.CashSession)
                .WithMany(s => s.CashCounts)
                .HasForeignKey(e => e.CashSessionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.CashSessionId, e.IsOpening });
        });

        // Configure CashCountDetail
        builder.Entity<CashCountDetail>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.HasOne(e => e.CashCount)
                .WithMany(c => c.Details)
                .HasForeignKey(e => e.CashCountId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Wallet)
                .WithMany()
                .HasForeignKey(e => e.WalletId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Configure Transaction
        builder.Entity<Transaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.Type)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(50);
            entity.Property(e => e.Currency).IsRequired().HasMaxLength(3);
            entity.HasOne(e => e.CashSession)
                .WithMany(s => s.Transactions)
                .HasForeignKey(e => e.CashSessionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.FromWallet)
                .WithMany(w => w.TransactionsFrom)
                .HasForeignKey(e => e.FromWalletId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.ToWallet)
                .WithMany(w => w.TransactionsTo)
                .HasForeignKey(e => e.ToWalletId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.CashSessionId, e.CreatedAt });
            entity.HasIndex(e => new { e.FromWalletId, e.CreatedAt });
        });

        // Configure Discrepancy
        builder.Entity<Discrepancy>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ExpectedAmount).HasPrecision(18, 2);
            entity.Property(e => e.ActualAmount).HasPrecision(18, 2);
            entity.Property(e => e.Variance).HasPrecision(18, 2);
            entity.Property(e => e.Status)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(50);
            entity.HasOne(e => e.CashSession)
                .WithMany(s => s.Discrepancies)
                .HasForeignKey(e => e.CashSessionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.CashCount)
                .WithMany()
                .HasForeignKey(e => e.CashCountId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.Status, e.CashSessionId });
        });

        // Configure AuditLog
        builder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EntityType).IsRequired().HasMaxLength(128);
            entity.Property(e => e.Action).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => new { e.EntityType, e.EntityId });
            entity.HasIndex(e => new { e.UserId, e.CreatedAt });
            entity.HasIndex(e => e.CreatedAt);
        });
    }
}
