using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EastSeat.Agenti.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBankReconciliationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BankRuns_AgentId",
                table: "BankRuns");

            migrationBuilder.AddColumn<long>(
                name: "CustomerTransactionId",
                table: "PendingTransactions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OperationalClosedAt",
                table: "CashSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RegisterVersion",
                table: "CashSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "CashSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "WorkflowVersion",
                table: "CashSessions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Legacy");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ObservedAt",
                table: "CashCountDetails",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CommandId",
                table: "BankRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OccurredAt",
                table: "BankRuns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BankAccounts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BranchId = table.Column<long>(type: "bigint", nullable: false),
                    WalletId = table.Column<long>(type: "bigint", nullable: false),
                    BankName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AccountIdentifier = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BalanceBasis = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ImportProfileKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ImportProfileVerified = table.Column<bool>(type: "boolean", nullable: false),
                    WorkflowEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankAccounts_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankAccounts_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashCountCorrectionRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CashCountId = table.Column<long>(type: "bigint", nullable: false),
                    AgentId = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    AllowedMovementIdsJson = table.Column<string>(type: "text", nullable: false),
                    AllowMissingTransactions = table.Column<bool>(type: "boolean", nullable: false),
                    IsOpen = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashCountCorrectionRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashCountCorrectionRequests_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashCountCorrectionRequests_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashCountCorrectionRequests_CashCounts_CashCountId",
                        column: x => x.CashCountId,
                        principalTable: "CashCounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashCountSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CashCountId = table.Column<long>(type: "bigint", nullable: false),
                    RegisterVersion = table.Column<int>(type: "integer", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    WalletsJson = table.Column<string>(type: "text", nullable: false),
                    CapturedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashCountSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashCountSnapshots_AspNetUsers_CapturedByUserId",
                        column: x => x.CapturedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashCountSnapshots_CashCounts_CashCountId",
                        column: x => x.CashCountId,
                        principalTable: "CashCounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankEvidenceFiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BankAccountId = table.Column<long>(type: "bigint", nullable: false),
                    UploadKey = table.Column<Guid>(type: "uuid", nullable: false),
                    IsStatement = table.Column<bool>(type: "boolean", nullable: false),
                    FileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankEvidenceFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankEvidenceFiles_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankEvidenceFiles_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankReconciliationReviews",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BankAccountId = table.Column<long>(type: "bigint", nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankReconciliationReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankReconciliationReviews_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankReconciliationReviews_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerTransactions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BankAccountId = table.Column<long>(type: "bigint", nullable: false),
                    AgentId = table.Column<long>(type: "bigint", nullable: false),
                    OriginatingSessionId = table.Column<long>(type: "bigint", nullable: false),
                    CashWalletId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CustomerIdentifier = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CommandId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerTransactions_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerTransactions_AspNetUsers_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerTransactions_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerTransactions_CashSessions_OriginatingSessionId",
                        column: x => x.OriginatingSessionId,
                        principalTable: "CashSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerTransactions_Wallets_CashWalletId",
                        column: x => x.CashWalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningBankVariances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BankAccountId = table.Column<long>(type: "bigint", nullable: false),
                    OpeningSnapshotId = table.Column<long>(type: "bigint", nullable: false),
                    PreviousClosingSnapshotId = table.Column<long>(type: "bigint", nullable: true),
                    PreviousBankBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ObservedBankBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PreviousObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningBankVariances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpeningBankVariances_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningBankVariances_CashCountSnapshots_OpeningSnapshotId",
                        column: x => x.OpeningSnapshotId,
                        principalTable: "CashCountSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningBankVariances_CashCountSnapshots_PreviousClosingSnap~",
                        column: x => x.PreviousClosingSnapshotId,
                        principalTable: "CashCountSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessionFloatSettlements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SnapshotId = table.Column<long>(type: "bigint", nullable: false),
                    CashCountId = table.Column<long>(type: "bigint", nullable: false),
                    VaultTransactionId = table.Column<long>(type: "bigint", nullable: false),
                    Stage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    VaultAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReportedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BankVariance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PostedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionFloatSettlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionFloatSettlements_AspNetUsers_PostedByUserId",
                        column: x => x.PostedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionFloatSettlements_CashCountSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "CashCountSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionFloatSettlements_CashCounts_CashCountId",
                        column: x => x.CashCountId,
                        principalTable: "CashCounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionFloatSettlements_VaultTransactions_VaultTransactionId",
                        column: x => x.VaultTransactionId,
                        principalTable: "VaultTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankStatementImports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BankAccountId = table.Column<long>(type: "bigint", nullable: false),
                    EvidenceFileId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ProfileKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProfileVersion = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CoverageFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CoverageTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OpeningBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ClosingBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CoverageVerified = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    UploadedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CommittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatementImports_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankStatementImports_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankStatementImports_BankEvidenceFiles_EvidenceFileId",
                        column: x => x.EvidenceFileId,
                        principalTable: "BankEvidenceFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankReconciliationResults",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReviewId = table.Column<long>(type: "bigint", nullable: false),
                    CashSessionId = table.Column<long>(type: "bigint", nullable: false),
                    SnapshotId = table.Column<long>(type: "bigint", nullable: false),
                    RegisterVersion = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    BasisHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReportJson = table.Column<string>(type: "text", nullable: false),
                    SupersedesResultId = table.Column<long>(type: "bigint", nullable: true),
                    ReviewedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankReconciliationResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankReconciliationResults_AspNetUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankReconciliationResults_BankReconciliationResults_Superse~",
                        column: x => x.SupersedesResultId,
                        principalTable: "BankReconciliationResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankReconciliationResults_BankReconciliationReviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "BankReconciliationReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankReconciliationResults_CashCountSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "CashCountSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankReconciliationResults_CashSessions_CashSessionId",
                        column: x => x.CashSessionId,
                        principalTable: "CashSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerMovements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerTransactionId = table.Column<long>(type: "bigint", nullable: false),
                    CashSessionId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CashEffect = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BankEffect = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BankReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    EvidenceFileId = table.Column<long>(type: "bigint", nullable: true),
                    RelatedMovementId = table.Column<long>(type: "bigint", nullable: true),
                    SupersedesMovementId = table.Column<long>(type: "bigint", nullable: true),
                    CorrectionRequestId = table.Column<long>(type: "bigint", nullable: true),
                    ReversalVerifiedByAgent = table.Column<bool>(type: "boolean", nullable: false),
                    CommandId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerMovements_AspNetUsers_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerMovements_BankEvidenceFiles_EvidenceFileId",
                        column: x => x.EvidenceFileId,
                        principalTable: "BankEvidenceFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerMovements_CashCountCorrectionRequests_CorrectionReq~",
                        column: x => x.CorrectionRequestId,
                        principalTable: "CashCountCorrectionRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerMovements_CashSessions_CashSessionId",
                        column: x => x.CashSessionId,
                        principalTable: "CashSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerMovements_CustomerMovements_RelatedMovementId",
                        column: x => x.RelatedMovementId,
                        principalTable: "CustomerMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerMovements_CustomerMovements_SupersedesMovementId",
                        column: x => x.SupersedesMovementId,
                        principalTable: "CustomerMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerMovements_CustomerTransactions_CustomerTransactionId",
                        column: x => x.CustomerTransactionId,
                        principalTable: "CustomerTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankStatementRows",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportId = table.Column<long>(type: "bigint", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    SourceLocation = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RawData = table.Column<string>(type: "text", nullable: false),
                    TransactionNumber = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CustomerIdentifier = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Narration = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    SignedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RunningBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ValueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    HasPreciseTimestamp = table.Column<bool>(type: "boolean", nullable: false),
                    SupersedesRowId = table.Column<long>(type: "bigint", nullable: true),
                    CorrectionReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CorrectedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CorrectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatementRows_AspNetUsers_CorrectedByUserId",
                        column: x => x.CorrectedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankStatementRows_BankStatementImports_ImportId",
                        column: x => x.ImportId,
                        principalTable: "BankStatementImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankStatementRows_BankStatementRows_SupersedesRowId",
                        column: x => x.SupersedesRowId,
                        principalTable: "BankStatementRows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankTransactions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BankAccountId = table.Column<long>(type: "bigint", nullable: false),
                    AcceptedRowId = table.Column<long>(type: "bigint", nullable: false),
                    ReliableIdentity = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Classification = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ClassificationReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RelatedCustomerTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankTransactions_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTransactions_BankStatementRows_AcceptedRowId",
                        column: x => x.AcceptedRowId,
                        principalTable: "BankStatementRows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTransactions_CustomerTransactions_RelatedCustomerTransa~",
                        column: x => x.RelatedCustomerTransactionId,
                        principalTable: "CustomerTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankReconciliationDecisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BankAccountId = table.Column<long>(type: "bigint", nullable: false),
                    ReviewId = table.Column<long>(type: "bigint", nullable: true),
                    CashSessionId = table.Column<long>(type: "bigint", nullable: true),
                    BankTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DetailsJson = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    PerformedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    PerformedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankReconciliationDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankReconciliationDecisions_AspNetUsers_PerformedByUserId",
                        column: x => x.PerformedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankReconciliationDecisions_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankReconciliationDecisions_BankReconciliationReviews_Revie~",
                        column: x => x.ReviewId,
                        principalTable: "BankReconciliationReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankReconciliationDecisions_BankTransactions_BankTransactio~",
                        column: x => x.BankTransactionId,
                        principalTable: "BankTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankReconciliationDecisions_CashSessions_CashSessionId",
                        column: x => x.CashSessionId,
                        principalTable: "CashSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankReconciliationExceptions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BankAccountId = table.Column<long>(type: "bigint", nullable: false),
                    CashSessionId = table.Column<long>(type: "bigint", nullable: false),
                    CustomerTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    PendingTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    BankTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Explanation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    EvidenceFileId = table.Column<long>(type: "bigint", nullable: false),
                    IsResolved = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    RecordedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankReconciliationExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankReconciliationExceptions_AspNetUsers_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankReconciliationExceptions_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankReconciliationExceptions_BankEvidenceFiles_EvidenceFile~",
                        column: x => x.EvidenceFileId,
                        principalTable: "BankEvidenceFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankReconciliationExceptions_BankTransactions_BankTransacti~",
                        column: x => x.BankTransactionId,
                        principalTable: "BankTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankReconciliationExceptions_CashSessions_CashSessionId",
                        column: x => x.CashSessionId,
                        principalTable: "CashSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankReconciliationExceptions_CustomerTransactions_CustomerT~",
                        column: x => x.CustomerTransactionId,
                        principalTable: "CustomerTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankReconciliationExceptions_PendingTransactions_PendingTra~",
                        column: x => x.PendingTransactionId,
                        principalTable: "PendingTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankReconciliationMatches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BankTransactionId = table.Column<long>(type: "bigint", nullable: false),
                    CustomerMovementId = table.Column<long>(type: "bigint", nullable: true),
                    BankRunId = table.Column<long>(type: "bigint", nullable: true),
                    WalletAdjustmentId = table.Column<long>(type: "bigint", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    MatchedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    MatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankReconciliationMatches", x => x.Id);
                    table.CheckConstraint("CK_BankReconciliationMatch_OneSource", "(CASE WHEN \"CustomerMovementId\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"BankRunId\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"WalletAdjustmentId\" IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_BankReconciliationMatches_AspNetUsers_MatchedByUserId",
                        column: x => x.MatchedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankReconciliationMatches_BankRuns_BankRunId",
                        column: x => x.BankRunId,
                        principalTable: "BankRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankReconciliationMatches_BankTransactions_BankTransactionId",
                        column: x => x.BankTransactionId,
                        principalTable: "BankTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankReconciliationMatches_CustomerMovements_CustomerMovemen~",
                        column: x => x.CustomerMovementId,
                        principalTable: "CustomerMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankReconciliationMatches_WalletAdjustments_WalletAdjustmen~",
                        column: x => x.WalletAdjustmentId,
                        principalTable: "WalletAdjustments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankStatementOccurrences",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BankTransactionId = table.Column<long>(type: "bigint", nullable: false),
                    StatementRowId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementOccurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatementOccurrences_BankStatementRows_StatementRowId",
                        column: x => x.StatementRowId,
                        principalTable: "BankStatementRows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankStatementOccurrences_BankTransactions_BankTransactionId",
                        column: x => x.BankTransactionId,
                        principalTable: "BankTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingTransactions_CustomerTransactionId",
                table: "PendingTransactions",
                column: "CustomerTransactionId",
                unique: true,
                filter: "\"CustomerTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BankRuns_AgentId_CommandId",
                table: "BankRuns",
                columns: new[] { "AgentId", "CommandId" },
                unique: true,
                filter: "\"CommandId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_BranchId",
                table: "BankAccounts",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_WalletId",
                table: "BankAccounts",
                column: "WalletId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankEvidenceFiles_BankAccountId_ContentHash",
                table: "BankEvidenceFiles",
                columns: new[] { "BankAccountId", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "IX_BankEvidenceFiles_UploadedByUserId_UploadKey",
                table: "BankEvidenceFiles",
                columns: new[] { "UploadedByUserId", "UploadKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationDecisions_BankAccountId_PerformedAt",
                table: "BankReconciliationDecisions",
                columns: new[] { "BankAccountId", "PerformedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationDecisions_BankTransactionId",
                table: "BankReconciliationDecisions",
                column: "BankTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationDecisions_CashSessionId",
                table: "BankReconciliationDecisions",
                column: "CashSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationDecisions_PerformedByUserId",
                table: "BankReconciliationDecisions",
                column: "PerformedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationDecisions_ReviewId",
                table: "BankReconciliationDecisions",
                column: "ReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationExceptions_BankAccountId_CashSessionId_Is~",
                table: "BankReconciliationExceptions",
                columns: new[] { "BankAccountId", "CashSessionId", "IsResolved" });

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationExceptions_BankTransactionId",
                table: "BankReconciliationExceptions",
                column: "BankTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationExceptions_CashSessionId",
                table: "BankReconciliationExceptions",
                column: "CashSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationExceptions_CustomerTransactionId",
                table: "BankReconciliationExceptions",
                column: "CustomerTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationExceptions_EvidenceFileId",
                table: "BankReconciliationExceptions",
                column: "EvidenceFileId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationExceptions_PendingTransactionId",
                table: "BankReconciliationExceptions",
                column: "PendingTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationExceptions_RecordedByUserId",
                table: "BankReconciliationExceptions",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationMatches_BankRunId",
                table: "BankReconciliationMatches",
                column: "BankRunId",
                unique: true,
                filter: "\"IsActive\" = TRUE AND \"BankRunId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationMatches_BankTransactionId",
                table: "BankReconciliationMatches",
                column: "BankTransactionId",
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationMatches_CustomerMovementId",
                table: "BankReconciliationMatches",
                column: "CustomerMovementId",
                unique: true,
                filter: "\"IsActive\" = TRUE AND \"CustomerMovementId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationMatches_MatchedByUserId",
                table: "BankReconciliationMatches",
                column: "MatchedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationMatches_WalletAdjustmentId",
                table: "BankReconciliationMatches",
                column: "WalletAdjustmentId",
                unique: true,
                filter: "\"IsActive\" = TRUE AND \"WalletAdjustmentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationResults_CashSessionId_ReviewedAt",
                table: "BankReconciliationResults",
                columns: new[] { "CashSessionId", "ReviewedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationResults_ReviewedByUserId",
                table: "BankReconciliationResults",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationResults_ReviewId",
                table: "BankReconciliationResults",
                column: "ReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationResults_SnapshotId",
                table: "BankReconciliationResults",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationResults_SupersedesResultId",
                table: "BankReconciliationResults",
                column: "SupersedesResultId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationReviews_BankAccountId_FromDate_ToDate",
                table: "BankReconciliationReviews",
                columns: new[] { "BankAccountId", "FromDate", "ToDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationReviews_CreatedByUserId",
                table: "BankReconciliationReviews",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImports_BankAccountId",
                table: "BankStatementImports",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImports_EvidenceFileId",
                table: "BankStatementImports",
                column: "EvidenceFileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImports_Status_CreatedAt",
                table: "BankStatementImports",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImports_UploadedByUserId",
                table: "BankStatementImports",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementOccurrences_BankTransactionId",
                table: "BankStatementOccurrences",
                column: "BankTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementOccurrences_StatementRowId",
                table: "BankStatementOccurrences",
                column: "StatementRowId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementRows_CorrectedByUserId",
                table: "BankStatementRows",
                column: "CorrectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementRows_ImportId_Sequence_Revision",
                table: "BankStatementRows",
                columns: new[] { "ImportId", "Sequence", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementRows_SupersedesRowId",
                table: "BankStatementRows",
                column: "SupersedesRowId",
                unique: true,
                filter: "\"SupersedesRowId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_AcceptedRowId",
                table: "BankTransactions",
                column: "AcceptedRowId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_BankAccountId_ReliableIdentity",
                table: "BankTransactions",
                columns: new[] { "BankAccountId", "ReliableIdentity" },
                unique: true,
                filter: "\"ReliableIdentity\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_RelatedCustomerTransactionId",
                table: "BankTransactions",
                column: "RelatedCustomerTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_CashCountCorrectionRequests_AgentId",
                table: "CashCountCorrectionRequests",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_CashCountCorrectionRequests_CashCountId",
                table: "CashCountCorrectionRequests",
                column: "CashCountId",
                unique: true,
                filter: "\"IsOpen\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_CashCountCorrectionRequests_RequestedByUserId",
                table: "CashCountCorrectionRequests",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CashCountSnapshots_CapturedByUserId",
                table: "CashCountSnapshots",
                column: "CapturedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CashCountSnapshots_CashCountId",
                table: "CashCountSnapshots",
                column: "CashCountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMovements_CashSessionId_OccurredAt",
                table: "CustomerMovements",
                columns: new[] { "CashSessionId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMovements_CorrectionRequestId",
                table: "CustomerMovements",
                column: "CorrectionRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMovements_CustomerTransactionId",
                table: "CustomerMovements",
                column: "CustomerTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMovements_EvidenceFileId",
                table: "CustomerMovements",
                column: "EvidenceFileId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMovements_RecordedByUserId_CommandId",
                table: "CustomerMovements",
                columns: new[] { "RecordedByUserId", "CommandId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMovements_RelatedMovementId",
                table: "CustomerMovements",
                column: "RelatedMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMovements_SupersedesMovementId",
                table: "CustomerMovements",
                column: "SupersedesMovementId",
                unique: true,
                filter: "\"SupersedesMovementId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerTransactions_AgentId_CommandId",
                table: "CustomerTransactions",
                columns: new[] { "AgentId", "CommandId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerTransactions_BankAccountId",
                table: "CustomerTransactions",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerTransactions_CashWalletId",
                table: "CustomerTransactions",
                column: "CashWalletId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerTransactions_OriginatingSessionId_BankAccountId",
                table: "CustomerTransactions",
                columns: new[] { "OriginatingSessionId", "BankAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerTransactions_RecordedByUserId",
                table: "CustomerTransactions",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBankVariances_BankAccountId_OpeningSnapshotId",
                table: "OpeningBankVariances",
                columns: new[] { "BankAccountId", "OpeningSnapshotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBankVariances_OpeningSnapshotId",
                table: "OpeningBankVariances",
                column: "OpeningSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBankVariances_PreviousClosingSnapshotId",
                table: "OpeningBankVariances",
                column: "PreviousClosingSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionFloatSettlements_CashCountId_Stage",
                table: "SessionFloatSettlements",
                columns: new[] { "CashCountId", "Stage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionFloatSettlements_PostedByUserId",
                table: "SessionFloatSettlements",
                column: "PostedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionFloatSettlements_SnapshotId",
                table: "SessionFloatSettlements",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionFloatSettlements_VaultTransactionId",
                table: "SessionFloatSettlements",
                column: "VaultTransactionId");

            migrationBuilder.AddForeignKey(
                name: "FK_PendingTransactions_CustomerTransactions_CustomerTransactio~",
                table: "PendingTransactions",
                column: "CustomerTransactionId",
                principalTable: "CustomerTransactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PendingTransactions_CustomerTransactions_CustomerTransactio~",
                table: "PendingTransactions");

            migrationBuilder.DropTable(
                name: "BankReconciliationDecisions");

            migrationBuilder.DropTable(
                name: "BankReconciliationExceptions");

            migrationBuilder.DropTable(
                name: "BankReconciliationMatches");

            migrationBuilder.DropTable(
                name: "BankReconciliationResults");

            migrationBuilder.DropTable(
                name: "BankStatementOccurrences");

            migrationBuilder.DropTable(
                name: "OpeningBankVariances");

            migrationBuilder.DropTable(
                name: "SessionFloatSettlements");

            migrationBuilder.DropTable(
                name: "CustomerMovements");

            migrationBuilder.DropTable(
                name: "BankReconciliationReviews");

            migrationBuilder.DropTable(
                name: "BankTransactions");

            migrationBuilder.DropTable(
                name: "CashCountSnapshots");

            migrationBuilder.DropTable(
                name: "CashCountCorrectionRequests");

            migrationBuilder.DropTable(
                name: "BankStatementRows");

            migrationBuilder.DropTable(
                name: "CustomerTransactions");

            migrationBuilder.DropTable(
                name: "BankStatementImports");

            migrationBuilder.DropTable(
                name: "BankEvidenceFiles");

            migrationBuilder.DropTable(
                name: "BankAccounts");

            migrationBuilder.DropIndex(
                name: "IX_PendingTransactions_CustomerTransactionId",
                table: "PendingTransactions");

            migrationBuilder.DropIndex(
                name: "IX_BankRuns_AgentId_CommandId",
                table: "BankRuns");

            migrationBuilder.DropColumn(
                name: "CustomerTransactionId",
                table: "PendingTransactions");

            migrationBuilder.DropColumn(
                name: "OperationalClosedAt",
                table: "CashSessions");

            migrationBuilder.DropColumn(
                name: "RegisterVersion",
                table: "CashSessions");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "CashSessions");

            migrationBuilder.DropColumn(
                name: "WorkflowVersion",
                table: "CashSessions");

            migrationBuilder.DropColumn(
                name: "ObservedAt",
                table: "CashCountDetails");

            migrationBuilder.DropColumn(
                name: "CommandId",
                table: "BankRuns");

            migrationBuilder.DropColumn(
                name: "OccurredAt",
                table: "BankRuns");

            migrationBuilder.CreateIndex(
                name: "IX_BankRuns_AgentId",
                table: "BankRuns",
                column: "AgentId");
        }
    }
}
