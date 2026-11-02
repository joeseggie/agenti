using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.Shared.Services;
using EastSeat.Agenti.Web.Data;
using Microsoft.EntityFrameworkCore;

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

    public void AddApprovedOpening(bool withSettlement = false, decimal bankAmount = 1_000_000m)
    {
        var opening = new CashCount
        {
            CashSessionId = Session.Id, AgentId = Agent.Id, IsOpening = true,
            Status = CashCountStatus.Approved, CountDate = Session.SessionDate,
            TotalAmount = 100_000m + bankAmount, CreatedAt = Session.OpenedAt,
            Details =
            [
                new() { WalletId = CashWallet.Id, Amount = 100_000m, ObservedAt = Session.OpenedAt },
                new() { WalletId = BankWallet.Id, Amount = bankAmount, ObservedAt = Session.OpenedAt }
            ]
        };
        Db.CashCounts.Add(opening);
        Db.SaveChanges();
        var snapshot = new CashCountSnapshot
        {
            CashCountId = opening.Id, TotalAmount = opening.TotalAmount,
            CapturedByUserId = Admin.Id, CapturedAt = Session.OpenedAt,
            WalletsJson = CountSnapshotCodec.Serialize(
            [
                new(CashWallet.Id, "Test cash", WalletTypeEnum.Cash, "UGX", 100_000m, Session.OpenedAt, null),
                new(BankWallet.Id, "Test bank", WalletTypeEnum.Bank, "UGX", bankAmount, Session.OpenedAt, Account.Id)
            ])
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

    public BankStatementImport AddImport()
    {
        var evidence = new BankEvidenceFile
        {
            BankAccountId = Account.Id, UploadKey = Guid.NewGuid(), IsStatement = true,
            FileName = "synthetic.csv", ContentType = "text/csv", ContentHash = new string('0', 64),
            Content = [1], UploadedByUserId = Admin.Id, UploadedAt = Now
        };
        Db.BankEvidenceFiles.Add(evidence);
        Db.SaveChanges();
        var import = new BankStatementImport
        {
            BankAccountId = Account.Id, EvidenceFileId = evidence.Id, ProfileKey = "synthetic",
            Status = StatementImportStatus.Committed, UploadedByUserId = Admin.Id, CreatedAt = Now
        };
        Db.BankStatementImports.Add(import);
        Db.SaveChanges();
        return import;
    }

    public void Dispose()
    {
        if (_ownsContext) Db.Dispose();
    }
}
