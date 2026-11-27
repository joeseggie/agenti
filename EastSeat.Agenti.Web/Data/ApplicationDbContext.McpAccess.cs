using EastSeat.Agenti.Shared.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EastSeat.Agenti.Web.Data;

public partial class ApplicationDbContext
{
    public DbSet<McpAccessToken> McpAccessTokens => Set<McpAccessToken>();

    private static void ConfigureMcpAccess(ModelBuilder builder)
    {
        builder.Entity<McpAccessToken>(entity =>
        {
            entity.Property(t => t.UserId).IsRequired().HasMaxLength(450);
            entity.Property(t => t.RevokedByUserId).HasMaxLength(450);
            entity.Property(t => t.Name).IsRequired().HasMaxLength(100);
            entity.Property(t => t.TokenHash).IsRequired().HasMaxLength(64);
            entity.Property(t => t.SecurityStamp).IsRequired().HasMaxLength(256);
            entity.HasIndex(t => t.TokenHash).IsUnique();
            entity.HasIndex(t => new { t.UserId, t.RequestId }).IsUnique();
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.RevokedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
