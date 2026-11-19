using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EastSeat.Agenti.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowDerivedStatementBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BalanceSource",
                table: "DailyBankStatements",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "BankReported");

            migrationBuilder.AddColumn<long>(
                name: "OpeningCountId",
                table: "DailyBankStatements",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowMissingRunningBalances",
                table: "BankAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_DailyBankStatements_OpeningCountId",
                table: "DailyBankStatements",
                column: "OpeningCountId");

            migrationBuilder.AddForeignKey(
                name: "FK_DailyBankStatements_CashCounts_OpeningCountId",
                table: "DailyBankStatements",
                column: "OpeningCountId",
                principalTable: "CashCounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "DailyBankStatements" WHERE "BalanceSource" = 'ApprovedOpening') THEN
                        RAISE EXCEPTION 'Cannot remove computed-balance provenance while derived statements exist.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_DailyBankStatements_CashCounts_OpeningCountId",
                table: "DailyBankStatements");

            migrationBuilder.DropIndex(
                name: "IX_DailyBankStatements_OpeningCountId",
                table: "DailyBankStatements");

            migrationBuilder.DropColumn(
                name: "BalanceSource",
                table: "DailyBankStatements");

            migrationBuilder.DropColumn(
                name: "OpeningCountId",
                table: "DailyBankStatements");

            migrationBuilder.DropColumn(
                name: "AllowMissingRunningBalances",
                table: "BankAccounts");
        }
    }
}
