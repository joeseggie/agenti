using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.BankStatements;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EastSeat.Agenti.UnitTests.Helpers;

internal class BankingScenario : IDisposable
{
    private readonly bool _ownsContext;
    public ApplicationDbContext Db { get; }
    public ApplicationUser User { get; }
    public ApplicationUser Admin { get; }
    public Agent Agent { get; }
    public BankAccount Account { get; }
    public Wallet CashWallet { get; }
    public Wallet BankWallet { get; }
    public CashSession Session { get; }
    public static DateTimeOffset Now { get; } = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    public BankingScenario(ApplicationDbContext context, bool ownsContext = false)
    {
        Db = context;
        _ownsContext = ownsContext;
        var branch = new Branch { Name = "Test branch", CreatedAt = Now };
        Db.Branches.Add(branch);
        Db.SaveChanges();
        User = new ApplicationUser
        {
            Id = "banking-agent", UserName = "banking-agent", FirstName = "Test", LastName = "Agent",
            Role = UserRole.Agent, BranchId = branch.Id, IsActive = true
        };
        Admin = new ApplicationUser
        {
            Id = "banking-admin", UserName = "banking-admin", FirstName = "Test", LastName = "Admin",
            Role = UserRole.Admin, BranchId = branch.Id, IsActive = true
        };
        Db.Users.AddRange(User, Admin);
        Db.SaveChanges();
        Agent = new Agent { UserId = User.Id, BranchId = branch.Id, Code = "TEST", IsActive = true, CreatedAt = Now };
        Db.Agents.Add(Agent);
        Db.SaveChanges();
        User.AgentId = Agent.Id;
        CashWallet = new Wallet
        {
            AgentId = Agent.Id, WalletTypeId = 1, Name = "Test cash", Currency = "UGX", Balance = 100_000m,
            IsActive = true, CreatedAt = Now
        };
        BankWallet = new Wallet
        {
            AgentId = Agent.Id, WalletTypeId = 4, Name = "Test bank", Currency = "UGX", Balance = 1_000_000m,
            IsActive = true, CreatedAt = Now
        };
        Db.Wallets.AddRange(CashWallet, BankWallet);
        Db.SaveChanges();
        Account = new BankAccount
        {
            BranchId = branch.Id, WalletId = BankWallet.Id, BankName = "Test bank",
            AccountIdentifier = "SYNTHETIC-ACCOUNT", CreatedAt = Now, BalanceBasis = BankBalanceBasis.Ledger
        };
        Session = new CashSession
        {
            BranchId = branch.Id, SessionDate = new DateOnly(2026, 11, 2), OpenedAt = Now.AddHours(-1),
            Status = CashSessionStatus.Open, WorkflowVersion = SessionWorkflowVersion.BankReconciliation
        };
        Db.AddRange(Account, Session);
        Db.SaveChanges();
    }

    public CustomerTransaction AddCustomerTransaction(decimal amount = 50_000m)
    {
        var transaction = new CustomerTransaction
        {
            BankAccountId = Account.Id, AgentId = Agent.Id, OriginatingSessionId = Session.Id,
            CashWalletId = CashWallet.Id, Kind = CustomerTransactionKind.Deposit, Amount = amount,
            CustomerIdentifier = "SYNTHETIC-CUSTOMER", CommandId = Guid.NewGuid(),
            RecordedByUserId = User.Id, OccurredAt = Now, RecordedAt = Now
        };
        Db.CustomerTransactions.Add(transaction);
        Db.SaveChanges();
        return transaction;
    }

    public void AddApprovedOpening(bool withSettlement = false, decimal bankAmount = 1_000_000m,
        IReadOnlyList<Wallet>? additionalWallets = null)
    {
        var wallets = new List<Wallet> { CashWallet, BankWallet };
        wallets.AddRange(additionalWallets ?? []);
        var bindings = Db.BankAccounts.ToDictionary(a => a.WalletId, a => a.Id);
        var snapshots = wallets.Select(w => new CountedWalletSnapshot(
            w.Id, w.Name, Db.WalletTypes.Find(w.WalletTypeId)!.Type, w.Currency,
            w.Id == BankWallet.Id ? bankAmount : w.Balance, Session.OpenedAt,
            bindings.TryGetValue(w.Id, out var accountId) ? accountId : null)).ToList();
        var opening = new CashCount
        {
            CashSessionId = Session.Id, AgentId = Agent.Id, IsOpening = true,
            Status = CashCountStatus.Approved, CountDate = Session.SessionDate,
            TotalAmount = snapshots.Sum(w => w.Amount), CreatedAt = Session.OpenedAt,
            Details = snapshots.Select(w => new CashCountDetail
                { WalletId = w.WalletId, Amount = w.Amount, ObservedAt = w.ObservedAt }).ToList()
        };
        Db.CashCounts.Add(opening);
        Db.SaveChanges();
        var snapshot = new CashCountSnapshot
        {
            CashCountId = opening.Id, TotalAmount = opening.TotalAmount,
            CapturedByUserId = Admin.Id, CapturedAt = Session.OpenedAt,
            WalletsJson = CountSnapshotCodec.Serialize(snapshots)
        };
        Db.CashCountSnapshots.Add(snapshot);
        Db.SaveChanges();
        if (withSettlement)
        {
            Db.SessionFloatSettlements.Add(new SessionFloatSettlement
            {
                CashCountId = opening.Id, SnapshotId = snapshot.Id, Stage = FloatSettlementStage.Opening,
                ReportedAmount = opening.TotalAmount, VaultAmount = opening.TotalAmount,
                PostedAt = Session.OpenedAt, PostedByUserId = Admin.Id
            });
            Db.SaveChanges();
        }
    }

    public Wallet AddWallet(string name, WalletTypeEnum category, decimal balance = 0m)
    {
        var type = new WalletType { Name = $"{name}-{Guid.NewGuid():N}", Type = category, IsActive = true, CreatedAt = Now };
        Db.WalletTypes.Add(type);
        Db.SaveChanges();
        var wallet = new Wallet
        {
            AgentId = Agent.Id, WalletTypeId = type.Id, Name = name, Currency = "UGX",
            Balance = balance, IsActive = true, CreatedAt = Now
        };
        Db.Wallets.Add(wallet);
        Db.SaveChanges();
        return wallet;
    }

    public BankAccount BindBankAccount(Wallet wallet)
    {
        var account = new BankAccount
        {
            BranchId = Account.BranchId, WalletId = wallet.Id, BankName = wallet.Name,
            AccountIdentifier = $"SYNTHETIC-{wallet.Id}", BalanceBasis = BankBalanceBasis.Ledger,
            CreatedAt = Now
        };
        Db.BankAccounts.Add(account);
        Db.SaveChanges();
        return account;
    }

    public CashCount AddSubmittedClosing(IReadOnlyDictionary<long, decimal>? amounts = null)
    {
        var opening = Db.CashCountSnapshots.Include(s => s.CashCount)
            .Single(s => s.CashCount!.CashSessionId == Session.Id && s.CashCount.IsOpening);
        var wallets = CountSnapshotCodec.Deserialize(opening.WalletsJson)
            .Select(w => w with { Amount = amounts?.GetValueOrDefault(w.WalletId, w.Amount) ?? w.Amount,
                ObservedAt = Now.AddMinutes(30) }).ToList();
        var count = new CashCount
        {
            CashSessionId = Session.Id, AgentId = Agent.Id, CountDate = Session.SessionDate,
            Status = CashCountStatus.PendingApproval, TotalAmount = wallets.Sum(w => w.Amount),
            CreatedAt = Now.AddMinutes(30)
        };
        Db.CashCounts.Add(count);
        Db.SaveChanges();
        var snapshot = new CashCountSnapshot
        {
            CashCountId = count.Id, TotalAmount = count.TotalAmount, RegisterVersion = Session.RegisterVersion,
            CapturedByUserId = User.Id, CapturedAt = count.CreatedAt, WalletsJson = CountSnapshotCodec.Serialize(wallets)
        };
        Db.CashCountSnapshots.Add(snapshot);
        Db.SaveChanges();
        Db.SessionFloatSettlements.Add(new SessionFloatSettlement
        {
            CashCountId = count.Id, SnapshotId = snapshot.Id, Stage = FloatSettlementStage.Closing,
            ReportedAmount = count.TotalAmount, VaultAmount = 0, PostedAt = snapshot.CapturedAt, PostedByUserId = User.Id
        });
        Session.Status = CashSessionStatus.Closed;
        Session.OperationalClosedAt = snapshot.CapturedAt;
        Db.SaveChanges();
        return count;
    }

    public async Task<DailyBankStatementDto> UploadStatementAsync(
        BankAccount account, IBusinessClock clock, string csv, long? previousId = null, string? reason = null)
    {
        var service = new DailyBankStatementService(Db, new BankingAccessService(Db, clock), clock,
            new UniversalBankCsvStatementProfile(), Options.Create(new BankEvidenceOptions()));
        using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        return await service.UploadAsync(Admin.Id,
            new DailyStatementUpload(account.Id, Session.SessionDate, true, previousId, reason), "synthetic.csv", content);
    }

    public static string StatementCsv(DateOnly date, decimal opening, decimal netChange = 0m)
    {
        var debit = netChange < 0 ? 1m - netChange : 1m;
        var credit = netChange > 0 ? 1m + netChange : 1m;
        return "TransactionDate,TransactionTime,Reference,Description,Debit,Credit,RunningBalance\n" +
            FormattableString.Invariant($"{date:yyyy-MM-dd},,SYNTHETIC-DEBIT,Synthetic debit,{debit:F2},,{opening - debit:F2}\n") +
            FormattableString.Invariant($"{date:yyyy-MM-dd},,SYNTHETIC-CREDIT,Synthetic credit,,{credit:F2},{opening + netChange:F2}\n");
    }

    public void Dispose()
    {
        if (_ownsContext) Db.Dispose();
    }
}

internal sealed class ScenarioTimeProvider(DateTimeOffset? now = null) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now ?? BankingScenario.Now;
}
