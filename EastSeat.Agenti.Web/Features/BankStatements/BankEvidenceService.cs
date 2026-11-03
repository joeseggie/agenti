using System.Security.Cryptography;
using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EastSeat.Agenti.Web.Features.BankStatements;

public sealed class BankEvidenceService(
    ApplicationDbContext db, IBankingAccessService access, IBusinessClock clock,
    IOptions<BankEvidenceOptions> options) : IBankEvidenceService
{
    public async Task<BankEvidenceInfoDto> UploadAsync(
        string userId, long bankAccountId, Guid uploadKey, string fileName, bool isStatement,
        Stream content, CancellationToken cancellationToken = default)
    {
        await access.RequireAccountAsync(userId, bankAccountId, isStatement, cancellationToken);
        if (uploadKey == Guid.Empty)
            throw new ArgumentException("An upload request identity is required.", nameof(uploadKey));
        var safeName = Path.GetFileName(fileName.Replace('\\', '/')).Trim();
        if (safeName.Length is 0 or > 256 || safeName.Any(char.IsControl))
            throw new ArgumentException("A valid filename is required.", nameof(fileName));
        var limit = options.Value.MaxFileBytes;
        if (limit <= 0 || limit > int.MaxValue)
            throw new BankingRuleException("The evidence upload size limit is not configured correctly.");

        using var buffer = new MemoryStream();
        var block = new byte[16 * 1024];
        while (true)
        {
            var count = await content.ReadAsync(block, cancellationToken);
            if (count == 0)
                break;
            if (buffer.Length + count > limit)
                throw new BankingRuleException($"The file exceeds the {limit:N0}-byte upload limit.");
            await buffer.WriteAsync(block.AsMemory(0, count), cancellationToken);
        }
        var bytes = buffer.ToArray();
        if (bytes.Length == 0)
            throw new BankingRuleException("An empty file cannot be uploaded as evidence.");
        var contentType = DetectContentType(safeName, bytes, isStatement);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        BankEvidenceInfoDto Replay(BankEvidenceFile existing)
        {
            if (existing.BankAccountId != bankAccountId || existing.ContentHash != hash ||
                existing.IsStatement != isStatement || existing.FileName != safeName)
                throw new BankingRuleException("This upload request identity was already used for different content.");
            return Map(existing);
        }

        if (await db.BankEvidenceFiles.AnyAsync(f => f.UploadedByUserId == userId && f.UploadKey == uploadKey, cancellationToken))
        {
            return Replay(await db.BankEvidenceFiles.AsNoTracking().SingleAsync(
                f => f.UploadedByUserId == userId && f.UploadKey == uploadKey, cancellationToken));
        }

        var evidence = new BankEvidenceFile
        {
            BankAccountId = bankAccountId, UploadKey = uploadKey, IsStatement = isStatement,
            FileName = safeName, ContentType = contentType, ContentHash = hash, Content = bytes,
            UploadedByUserId = userId, UploadedAt = clock.UtcNow
        };
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            db.BankEvidenceFiles.Add(evidence);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            db.Entry(evidence).State = EntityState.Detached;
            var committed = await db.BankEvidenceFiles.AsNoTracking().SingleOrDefaultAsync(
                f => f.UploadedByUserId == userId && f.UploadKey == uploadKey, cancellationToken);
            if (committed == null)
                throw;
            return Replay(committed);
        }
        return Map(evidence);
    }

    public async Task<BankEvidenceContentDto> DownloadAsync(
        string userId, long evidenceId, CancellationToken cancellationToken = default)
    {
        var metadata = await db.BankEvidenceFiles.AsNoTracking()
            .Where(f => f.Id == evidenceId)
            .Select(f => new { f.BankAccountId, f.IsStatement })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new BankingRuleException("Evidence file not found.");
        await access.RequireAccountAsync(userId, metadata.BankAccountId, metadata.IsStatement, cancellationToken);
        var file = await db.BankEvidenceFiles.AsNoTracking().SingleAsync(f => f.Id == evidenceId, cancellationToken);
        return new BankEvidenceContentDto(Map(file), file.Content);
    }

    private static string DetectContentType(string fileName, byte[] bytes, bool isStatement)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var data = bytes.AsSpan();
        if (extension == ".pdf" && data.StartsWith("%PDF-"u8))
            return "application/pdf";
        if (isStatement && extension == ".csv")
            return "text/csv";
        if (isStatement && extension == ".xlsx" && data.StartsWith(new byte[] { 0x50, 0x4b, 0x03, 0x04 }))
            return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        if (!isStatement && extension == ".png" &&
            data.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }))
            return "image/png";
        if (!isStatement && extension is ".jpg" or ".jpeg" &&
            data.StartsWith(new byte[] { 0xff, 0xd8, 0xff }))
            return "image/jpeg";
        throw new BankingRuleException(isStatement
            ? "Upload a CSV, XLSX, or PDF statement. Its bank layout must also pass import validation."
            : "Upload a JPEG, PNG, or PDF receipt with a valid file header.");
    }

    private static BankEvidenceInfoDto Map(BankEvidenceFile file) => new(
        file.Id, file.BankAccountId, file.FileName, file.ContentType, file.Content.LongLength,
        file.IsStatement, file.UploadedAt);
}
