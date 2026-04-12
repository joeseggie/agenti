using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EastSeat.Agenti.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBankRunsV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Denominations",
                table: "BankRuns");

            migrationBuilder.AddColumn<byte[]>(
                name: "ReceiptImage",
                table: "BankRuns",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptImageContentType",
                table: "BankRuns",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReceiptImage",
                table: "BankRuns");

            migrationBuilder.DropColumn(
                name: "ReceiptImageContentType",
                table: "BankRuns");

            migrationBuilder.AddColumn<string>(
                name: "Denominations",
                table: "BankRuns",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }
    }
}
