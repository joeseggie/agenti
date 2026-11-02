using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EastSeat.Agenti.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class CompleteBankReconciliationContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BankStatementImports_BankAccountId",
                table: "BankStatementImports");

            migrationBuilder.DropIndex(
                name: "IX_BankStatementImports_EvidenceFileId",
                table: "BankStatementImports");

            migrationBuilder.DropIndex(
                name: "IX_BankReconciliationResults_ReviewedByUserId",
                table: "BankReconciliationResults");

            migrationBuilder.AddColumn<string>(
                name: "BankReference",
                table: "WalletAdjustments",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CommandId",
                table: "WalletAdjustments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OccurredAt",
                table: "WalletAdjustments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "WalletAdjustments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "VaultTransactionId",
                table: "SessionFloatSettlements",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "PreviousClosingCountId",
                table: "OpeningBankVariances",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                table: "CustomerMovements",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "CustomerIdentifier",
                table: "CustomerMovements",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CustomerSettledAmount",
                table: "CustomerMovements",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "OccurrenceDate",
                table: "CustomerMovements",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<bool>(
                name: "ReferenceUnavailable",
                table: "CustomerMovements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SupportTicketNumber",
                table: "CustomerMovements",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransactionKind",
                table: "CustomerMovements",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Fingerprint",
                table: "BankTransactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "LedgerDate",
                table: "BankStatementRows",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateOnly>(
                name: "OccurrenceDate",
                table: "BankStatementRows",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<string>(
                name: "BalanceBasis",
                table: "BankStatementImports",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CommitRequestHash",
                table: "BankStatementImports",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CoverageDeclaredComplete",
                table: "BankStatementImports",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CoverageFromDate",
                table: "BankStatementImports",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CoverageToDate",
                table: "BankStatementImports",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "BankStatementImports",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "PreviousImportId",
                table: "BankStatementImports",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportedAccountIdentifier",
                table: "BankStatementImports",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "BankStatementImports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "BankRuns",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CommandId",
                table: "BankReconciliationResults",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                table: "BankReconciliationExceptions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "BankExplanationVerified",
                table: "BankReconciliationExceptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "BankRunId",
                table: "BankReconciliationExceptions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CustomerMovementId",
                table: "BankReconciliationExceptions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReflectedInClosingBalance",
                table: "BankReconciliationExceptions",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "WalletAdjustmentId",
                table: "BankReconciliationExceptions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankIdentifier",
                table: "BankAccounts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletAdjustments_AgentId_CommandId",
                table: "WalletAdjustments",
                columns: new[] { "AgentId", "CommandId" },
                unique: true,
                filter: "\"CommandId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBankVariances_PreviousClosingCountId",
                table: "OpeningBankVariances",
                column: "PreviousClosingCountId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMovements_BankReference",
                table: "CustomerMovements",
                column: "BankReference");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMovements_OccurrenceDate",
                table: "CustomerMovements",
                column: "OccurrenceDate");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_BankAccountId_Fingerprint",
                table: "BankTransactions",
                columns: new[] { "BankAccountId", "Fingerprint" });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementRows_LedgerDate",
                table: "BankStatementRows",
                column: "LedgerDate");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementRows_OccurrenceDate",
                table: "BankStatementRows",
                column: "OccurrenceDate");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementRows_Reference",
                table: "BankStatementRows",
                column: "Reference");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementRows_TransactionNumber",
                table: "BankStatementRows",
                column: "TransactionNumber");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImports_BankAccountId_CoverageFromDate_Coverag~",
                table: "BankStatementImports",
                columns: new[] { "BankAccountId", "CoverageFromDate", "CoverageToDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImports_EvidenceFileId_Revision",
                table: "BankStatementImports",
                columns: new[] { "EvidenceFileId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImports_PreviousImportId",
                table: "BankStatementImports",
                column: "PreviousImportId");

            migrationBuilder.CreateIndex(
                name: "IX_BankRuns_AgentId",
                table: "BankRuns",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationResults_ReviewedByUserId_CommandId",
                table: "BankReconciliationResults",
                columns: new[] { "ReviewedByUserId", "CommandId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationExceptions_BankRunId",
                table: "BankReconciliationExceptions",
                column: "BankRunId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationExceptions_CustomerMovementId",
                table: "BankReconciliationExceptions",
                column: "CustomerMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationExceptions_WalletAdjustmentId",
                table: "BankReconciliationExceptions",
                column: "WalletAdjustmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_BankReconciliationExceptions_BankRuns_BankRunId",
                table: "BankReconciliationExceptions",
                column: "BankRunId",
                principalTable: "BankRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BankReconciliationExceptions_CustomerMovements_CustomerMove~",
                table: "BankReconciliationExceptions",
                column: "CustomerMovementId",
                principalTable: "CustomerMovements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BankReconciliationExceptions_WalletAdjustments_WalletAdjust~",
                table: "BankReconciliationExceptions",
                column: "WalletAdjustmentId",
                principalTable: "WalletAdjustments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BankStatementImports_BankStatementImports_PreviousImportId",
                table: "BankStatementImports",
                column: "PreviousImportId",
                principalTable: "BankStatementImports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OpeningBankVariances_CashCounts_PreviousClosingCountId",
                table: "OpeningBankVariances",
                column: "PreviousClosingCountId",
                principalTable: "CashCounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BankReconciliationExceptions_BankRuns_BankRunId",
                table: "BankReconciliationExceptions");

            migrationBuilder.DropForeignKey(
                name: "FK_BankReconciliationExceptions_CustomerMovements_CustomerMove~",
                table: "BankReconciliationExceptions");

            migrationBuilder.DropForeignKey(
                name: "FK_BankReconciliationExceptions_WalletAdjustments_WalletAdjust~",
                table: "BankReconciliationExceptions");

            migrationBuilder.DropForeignKey(
                name: "FK_BankStatementImports_BankStatementImports_PreviousImportId",
                table: "BankStatementImports");

            migrationBuilder.DropForeignKey(
                name: "FK_OpeningBankVariances_CashCounts_PreviousClosingCountId",
                table: "OpeningBankVariances");

            migrationBuilder.DropIndex(
                name: "IX_WalletAdjustments_AgentId_CommandId",
                table: "WalletAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_OpeningBankVariances_PreviousClosingCountId",
                table: "OpeningBankVariances");

            migrationBuilder.DropIndex(
                name: "IX_CustomerMovements_BankReference",
                table: "CustomerMovements");

            migrationBuilder.DropIndex(
                name: "IX_CustomerMovements_OccurrenceDate",
                table: "CustomerMovements");

            migrationBuilder.DropIndex(
                name: "IX_BankTransactions_BankAccountId_Fingerprint",
                table: "BankTransactions");

            migrationBuilder.DropIndex(
                name: "IX_BankStatementRows_LedgerDate",
                table: "BankStatementRows");

            migrationBuilder.DropIndex(
                name: "IX_BankStatementRows_OccurrenceDate",
                table: "BankStatementRows");

            migrationBuilder.DropIndex(
                name: "IX_BankStatementRows_Reference",
                table: "BankStatementRows");

            migrationBuilder.DropIndex(
                name: "IX_BankStatementRows_TransactionNumber",
                table: "BankStatementRows");

            migrationBuilder.DropIndex(
                name: "IX_BankStatementImports_BankAccountId_CoverageFromDate_Coverag~",
                table: "BankStatementImports");

            migrationBuilder.DropIndex(
                name: "IX_BankStatementImports_EvidenceFileId_Revision",
                table: "BankStatementImports");

            migrationBuilder.DropIndex(
                name: "IX_BankStatementImports_PreviousImportId",
                table: "BankStatementImports");

            migrationBuilder.DropIndex(
                name: "IX_BankRuns_AgentId",
                table: "BankRuns");

            migrationBuilder.DropIndex(
                name: "IX_BankReconciliationResults_ReviewedByUserId_CommandId",
                table: "BankReconciliationResults");

            migrationBuilder.DropIndex(
                name: "IX_BankReconciliationExceptions_BankRunId",
                table: "BankReconciliationExceptions");

            migrationBuilder.DropIndex(
                name: "IX_BankReconciliationExceptions_CustomerMovementId",
                table: "BankReconciliationExceptions");

            migrationBuilder.DropIndex(
                name: "IX_BankReconciliationExceptions_WalletAdjustmentId",
                table: "BankReconciliationExceptions");

            migrationBuilder.DropColumn(
                name: "BankReference",
                table: "WalletAdjustments");

            migrationBuilder.DropColumn(
                name: "CommandId",
                table: "WalletAdjustments");

            migrationBuilder.DropColumn(
                name: "OccurredAt",
                table: "WalletAdjustments");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "WalletAdjustments");

            migrationBuilder.DropColumn(
                name: "PreviousClosingCountId",
                table: "OpeningBankVariances");

            migrationBuilder.DropColumn(
                name: "Amount",
                table: "CustomerMovements");

            migrationBuilder.DropColumn(
                name: "CustomerIdentifier",
                table: "CustomerMovements");

            migrationBuilder.DropColumn(
                name: "CustomerSettledAmount",
                table: "CustomerMovements");

            migrationBuilder.DropColumn(
                name: "OccurrenceDate",
                table: "CustomerMovements");

            migrationBuilder.DropColumn(
                name: "ReferenceUnavailable",
                table: "CustomerMovements");

            migrationBuilder.DropColumn(
                name: "SupportTicketNumber",
                table: "CustomerMovements");

            migrationBuilder.DropColumn(
                name: "TransactionKind",
                table: "CustomerMovements");

            migrationBuilder.DropColumn(
                name: "Fingerprint",
                table: "BankTransactions");

            migrationBuilder.DropColumn(
                name: "LedgerDate",
                table: "BankStatementRows");

            migrationBuilder.DropColumn(
                name: "OccurrenceDate",
                table: "BankStatementRows");

            migrationBuilder.DropColumn(
                name: "BalanceBasis",
                table: "BankStatementImports");

            migrationBuilder.DropColumn(
                name: "CommitRequestHash",
                table: "BankStatementImports");

            migrationBuilder.DropColumn(
                name: "CoverageDeclaredComplete",
                table: "BankStatementImports");

            migrationBuilder.DropColumn(
                name: "CoverageFromDate",
                table: "BankStatementImports");

            migrationBuilder.DropColumn(
                name: "CoverageToDate",
                table: "BankStatementImports");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "BankStatementImports");

            migrationBuilder.DropColumn(
                name: "PreviousImportId",
                table: "BankStatementImports");

            migrationBuilder.DropColumn(
                name: "ReportedAccountIdentifier",
                table: "BankStatementImports");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "BankStatementImports");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "BankRuns");

            migrationBuilder.DropColumn(
                name: "CommandId",
                table: "BankReconciliationResults");

            migrationBuilder.DropColumn(
                name: "Amount",
                table: "BankReconciliationExceptions");

            migrationBuilder.DropColumn(
                name: "BankExplanationVerified",
                table: "BankReconciliationExceptions");

            migrationBuilder.DropColumn(
                name: "BankRunId",
                table: "BankReconciliationExceptions");

            migrationBuilder.DropColumn(
                name: "CustomerMovementId",
                table: "BankReconciliationExceptions");

            migrationBuilder.DropColumn(
                name: "ReflectedInClosingBalance",
                table: "BankReconciliationExceptions");

            migrationBuilder.DropColumn(
                name: "WalletAdjustmentId",
                table: "BankReconciliationExceptions");

            migrationBuilder.DropColumn(
                name: "BankIdentifier",
                table: "BankAccounts");

            migrationBuilder.AlterColumn<long>(
                name: "VaultTransactionId",
                table: "SessionFloatSettlements",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

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
                name: "IX_BankReconciliationResults_ReviewedByUserId",
                table: "BankReconciliationResults",
                column: "ReviewedByUserId");
        }
    }
}
