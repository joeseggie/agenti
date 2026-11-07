using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EastSeat.Agenti.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUniversalBankCsvMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "OpeningBaseline",
                table: "BankStatementImports",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RequestedFromDate",
                table: "BankStatementImports",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RequestedToDate",
                table: "BankStatementImports",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SchemaHash",
                table: "BankStatementImports",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OpeningBaseline",
                table: "BankStatementImports");

            migrationBuilder.DropColumn(
                name: "RequestedFromDate",
                table: "BankStatementImports");

            migrationBuilder.DropColumn(
                name: "RequestedToDate",
                table: "BankStatementImports");

            migrationBuilder.DropColumn(
                name: "SchemaHash",
                table: "BankStatementImports");
        }
    }
}
