using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.Web.Data;

/// <summary>
/// Main database context for the Agenti ERP system.
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    // Domain entities
    public DbSet<Branch> Branches { get; set; }
    public DbSet<Vault> Vaults { get; set; }
    public DbSet<VaultTransaction> VaultTransactions { get; set; }
    public DbSet<Agent> Agents { get; set; }
    public DbSet<WalletType> WalletTypes { get; set; }
    public DbSet<Wallet> Wallets { get; set; }
    public DbSet<CashSession> CashSessions { get; set; }
    public DbSet<CashCount> CashCounts { get; set; }
    public DbSet<CashCountDetail> CashCountDetails { get; set; }
    public DbSet<Transaction> Transactions { get; set; }
    public DbSet<Discrepancy> Discrepancies { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<UserAuditLog> UserAuditLogs { get; set; }

    public override int SaveChanges()
    {
        return SaveChangesAsync().GetAwaiter().GetResult();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var newBranches = ChangeTracker.Entries<Branch>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList();

        if (!newBranches.Any())
        {
            return await base.SaveChangesAsync(cancellationToken);
        }

        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);

        var result = await base.SaveChangesAsync(cancellationToken);

        foreach (var branch in newBranches)
        {
            var hasVault = await Vaults.AnyAsync(v => v.BranchId == branch.Id, cancellationToken);
            if (!hasVault)
            {
                Vaults.Add(new Vault
                {
                    BranchId = branch.Id,
                    CurrentBalance = 0,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
        }

        result += await base.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return result;
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Configure Branch
        builder.Entity<Branch>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(256);
        });

        // Configure Vault
        builder.Entity<Vault>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CurrentBalance).HasPrecision(18, 2);
            entity.HasIndex(e => e.BranchId).IsUnique();

            entity.HasOne(v => v.Branch)
                .WithOne(b => b.Vault)
                .HasForeignKey<Vault>(v => v.BranchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure VaultTransaction
        builder.Entity<VaultTransaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.BalanceAfter).HasPrecision(18, 2);
            entity.Property(e => e.Type)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(50);
            entity.Property(e => e.Status)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(50);
            entity.Property(e => e.CreatedByUserId).IsRequired().HasMaxLength(450);
            entity.Property(e => e.ApprovedByUserId).HasMaxLength(450);
            entity.Property(e => e.Notes).HasMaxLength(1000);

            entity.HasIndex(e => e.VaultId);
            entity.HasIndex(e => new { e.VaultId, e.CreatedAt });
            entity.HasIndex(e => new { e.Status, e.ExpiresAt });

            entity.HasOne(e => e.Vault)
                .WithMany(v => v.Transactions)
                .HasForeignKey(e => e.VaultId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CashSession)
                .WithMany()
                .HasForeignKey(e => e.CashSessionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.CreatedByUser)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ApprovedByUser)
                .WithMany()
                .HasForeignKey(e => e.ApprovedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Configure Agent
        builder.Entity<Agent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired().HasMaxLength(450);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.HasIndex(e => e.BranchId);

            // Configure Agent -> User relationship
            entity.HasOne(a => a.User)
                .WithOne(u => u.Agent)
                .HasForeignKey<Agent>(a => a.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Configure ApplicationUser
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.LastName).IsRequired().HasMaxLength(100);
        });

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
            entity.HasOne(e => e.Agent)
                .WithMany(a => a.Wallets)
                .HasForeignKey(e => e.AgentId)
                .OnDelete(DeleteBehavior.Restrict);
            // Each agent can only have one wallet of each type
            entity.HasIndex(e => new { e.AgentId, e.WalletTypeId }).IsUnique();
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
            entity.HasOne(e => e.Agent)
                .WithMany(a => a.CashSessions)
                .HasForeignKey(e => e.AgentId)
                .OnDelete(DeleteBehavior.Restrict);
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

        // Configure UserAuditLog
        builder.Entity<UserAuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired().HasMaxLength(450);
            entity.Property(e => e.PerformedByUserId).IsRequired().HasMaxLength(450);
            entity.Property(e => e.Action)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(50);
            entity.Property(e => e.OldValue).HasMaxLength(1000);
            entity.Property(e => e.NewValue).HasMaxLength(1000);
            entity.Property(e => e.PerformedAt).IsRequired();

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.PerformedByUser)
                .WithMany()
                .HasForeignKey(e => e.PerformedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.UserId, e.PerformedAt });
            entity.HasIndex(e => e.PerformedAt);
        });

        // Seed default wallet types
        SeedWalletTypes(builder);
    }

    private static void SeedWalletTypes(ModelBuilder builder)
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        builder.Entity<WalletType>().HasData(
            new WalletType
            {
                Id = 1,
                Name = "Cash",
                Description = "Physical cash in drawer or safe",
                Type = WalletTypeEnum.Cash,
                IsSystem = true,
                IsActive = true,
                SupportsDenominations = true,
                CreatedAt = now
            },
            new WalletType
            {
                Id = 2,
                Name = "MTN Mobile Money",
                Description = "MTN Mobile Money float",
                Type = WalletTypeEnum.MobileMoney,
                IsSystem = true,
                IsActive = true,
                SupportsDenominations = false,
                CreatedAt = now
            },
            new WalletType
            {
                Id = 3,
                Name = "Airtel Money",
                Description = "Airtel Money float",
                Type = WalletTypeEnum.MobileMoney,
                IsSystem = true,
                IsActive = true,
                SupportsDenominations = false,
                CreatedAt = now
            },
            new WalletType
            {
                Id = 4,
                Name = "Bank Account",
                Description = "Linked bank account for transfers",
                Type = WalletTypeEnum.Bank,
                IsSystem = true,
                IsActive = true,
                SupportsDenominations = false,
                CreatedAt = now
            }
        );
    }
}
