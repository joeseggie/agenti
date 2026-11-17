using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EastSeat.Agenti.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceClosingReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reconciliation_archive");

            migrationBuilder.RenameTable(
                name: "CashCountCorrectionRequests",
                newName: "CashCountCorrectionRequests",
                newSchema: "reconciliation_archive");

            migrationBuilder.RenameTable(
                name: "BankTransactions",
                newName: "BankTransactions",
                newSchema: "reconciliation_archive");

            migrationBuilder.RenameTable(
                name: "BankStatementRows",
                newName: "BankStatementRows",
                newSchema: "reconciliation_archive");

            migrationBuilder.RenameTable(
                name: "BankStatementOccurrences",
                newName: "BankStatementOccurrences",
                newSchema: "reconciliation_archive");

            migrationBuilder.RenameTable(
                name: "BankStatementImports",
                newName: "BankStatementImports",
                newSchema: "reconciliation_archive");

            migrationBuilder.RenameTable(
                name: "BankReconciliationReviews",
                newName: "BankReconciliationReviews",
                newSchema: "reconciliation_archive");

            migrationBuilder.RenameTable(
                name: "BankReconciliationResults",
                newName: "BankReconciliationResults",
                newSchema: "reconciliation_archive");

            migrationBuilder.RenameTable(
                name: "BankReconciliationMatches",
                newName: "BankReconciliationMatches",
                newSchema: "reconciliation_archive");

            migrationBuilder.RenameTable(
                name: "BankReconciliationExceptions",
                newName: "BankReconciliationExceptions",
                newSchema: "reconciliation_archive");

            migrationBuilder.RenameTable(
                name: "BankReconciliationDecisions",
                newName: "BankReconciliationDecisions",
                newSchema: "reconciliation_archive");

            migrationBuilder.CreateTable(
                name: "ClosingReconciliationRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClosingCountId = table.Column<long>(type: "bigint", nullable: false),
                    OpeningCountId = table.Column<long>(type: "bigint", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    InputHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    PerformedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    PerformedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClosingReconciliationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClosingReconciliationRuns_AspNetUsers_PerformedByUserId",
                        column: x => x.PerformedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClosingReconciliationRuns_CashCounts_ClosingCountId",
                        column: x => x.ClosingCountId,
                        principalTable: "CashCounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClosingReconciliationRuns_CashCounts_OpeningCountId",
                        column: x => x.OpeningCountId,
                        principalTable: "CashCounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DailyBankStatements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BankAccountId = table.Column<long>(type: "bigint", nullable: false),
                    StatementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    SupersedesId = table.Column<long>(type: "bigint", nullable: true),
                    ReplacementReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    AccountIdentifier = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SchemaHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDebits = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalCredits = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UploadedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyBankStatements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyBankStatements_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyBankStatements_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyBankStatements_DailyBankStatements_SupersedesId",
                        column: x => x.SupersedesId,
                        principalTable: "DailyBankStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClosingApprovalDecisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClosingCountId = table.Column<long>(type: "bigint", nullable: false),
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    IsException = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ApprovedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClosingApprovalDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClosingApprovalDecisions_AspNetUsers_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClosingApprovalDecisions_CashCounts_ClosingCountId",
                        column: x => x.ClosingCountId,
                        principalTable: "CashCounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClosingApprovalDecisions_ClosingReconciliationRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "ClosingReconciliationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClosingWalletResults",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    WalletId = table.Column<long>(type: "bigint", nullable: false),
                    BankAccountId = table.Column<long>(type: "bigint", nullable: false),
                    StatementId = table.Column<long>(type: "bigint", nullable: false),
                    WalletName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    BankName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AccountIdentifier = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDebits = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalCredits = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpectedClosing = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SubmittedClosing = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    StatementOpening = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    StatementClosing = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Difference = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    Explanation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClosingWalletResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClosingWalletResults_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClosingWalletResults_ClosingReconciliationRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "ClosingReconciliationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClosingWalletResults_DailyBankStatements_StatementId",
                        column: x => x.StatementId,
                        principalTable: "DailyBankStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClosingWalletResults_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DailyBankStatementRows",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StatementId = table.Column<long>(type: "bigint", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TransactionTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Debit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Credit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RunningBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyBankStatementRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyBankStatementRows_DailyBankStatements_StatementId",
                        column: x => x.StatementId,
                        principalTable: "DailyBankStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClosingApprovalDecisions_ApprovedByUserId",
                table: "ClosingApprovalDecisions",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClosingApprovalDecisions_ClosingCountId",
                table: "ClosingApprovalDecisions",
                column: "ClosingCountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClosingApprovalDecisions_RunId",
                table: "ClosingApprovalDecisions",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_ClosingReconciliationRuns_ClosingCountId",
                table: "ClosingReconciliationRuns",
                column: "ClosingCountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClosingReconciliationRuns_OpeningCountId",
                table: "ClosingReconciliationRuns",
                column: "OpeningCountId");

            migrationBuilder.CreateIndex(
                name: "IX_ClosingReconciliationRuns_PerformedByUserId",
                table: "ClosingReconciliationRuns",
                column: "PerformedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClosingWalletResults_BankAccountId",
                table: "ClosingWalletResults",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ClosingWalletResults_RunId_WalletId",
                table: "ClosingWalletResults",
                columns: new[] { "RunId", "WalletId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClosingWalletResults_StatementId",
                table: "ClosingWalletResults",
                column: "StatementId");

            migrationBuilder.CreateIndex(
                name: "IX_ClosingWalletResults_WalletId",
                table: "ClosingWalletResults",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyBankStatementRows_StatementId_Sequence",
                table: "DailyBankStatementRows",
                columns: new[] { "StatementId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyBankStatements_BankAccountId_StatementDate_Revision",
                table: "DailyBankStatements",
                columns: new[] { "BankAccountId", "StatementDate", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyBankStatements_SupersedesId",
                table: "DailyBankStatements",
                column: "SupersedesId",
                unique: true,
                filter: "\"SupersedesId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DailyBankStatements_UploadedByUserId",
                table: "DailyBankStatements",
                column: "UploadedByUserId");

            migrationBuilder.Sql("""
                CREATE FUNCTION reconciliation_archive.reject_history_write() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Legacy reconciliation history is archived and read-only';
                END;
                $$;
                DO $$
                DECLARE table_name text;
                BEGIN
                    FOREACH table_name IN ARRAY ARRAY[
                        'CashCountCorrectionRequests', 'BankTransactions', 'BankStatementRows',
                        'BankStatementOccurrences', 'BankStatementImports', 'BankReconciliationReviews',
                        'BankReconciliationResults', 'BankReconciliationMatches',
                        'BankReconciliationExceptions', 'BankReconciliationDecisions']
                    LOOP
                        EXECUTE format('CREATE TRIGGER reject_archived_write BEFORE INSERT OR UPDATE OR DELETE ON reconciliation_archive.%I FOR EACH ROW EXECUTE FUNCTION reconciliation_archive.reject_history_write()', table_name);
                        EXECUTE format('CREATE TRIGGER reject_archived_truncate BEFORE TRUNCATE ON reconciliation_archive.%I FOR EACH STATEMENT EXECUTE FUNCTION reconciliation_archive.reject_history_write()', table_name);
                    END LOOP;
                END;
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Reverting would discard financial history. Use a reviewed, data-preserving forward migration.");
        }
    }
}
