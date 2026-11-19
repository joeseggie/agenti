using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EastSeat.Agenti.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStatementReconciliationWaiver : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresStatementReconciliation",
                table: "WalletTypes",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AlterColumn<long>(
                name: "StatementId",
                table: "ClosingWalletResults",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<bool>(
                name: "StatementWaived",
                table: "ClosingWalletResults",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "WalletTypeReconciliationAudits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WalletTypeId = table.Column<long>(type: "bigint", nullable: false),
                    Required = table.Column<bool>(type: "boolean", nullable: false),
                    ChangedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTypeReconciliationAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WalletTypeReconciliationAudits_AspNetUsers_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WalletTypeReconciliationAudits_WalletTypes_WalletTypeId",
                        column: x => x.WalletTypeId,
                        principalTable: "WalletTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "WalletTypes",
                keyColumn: "Id",
                keyValue: 1L,
                column: "RequiresStatementReconciliation",
                value: true);

            migrationBuilder.UpdateData(
                table: "WalletTypes",
                keyColumn: "Id",
                keyValue: 2L,
                column: "RequiresStatementReconciliation",
                value: true);

            migrationBuilder.UpdateData(
                table: "WalletTypes",
                keyColumn: "Id",
                keyValue: 3L,
                column: "RequiresStatementReconciliation",
                value: true);

            migrationBuilder.UpdateData(
                table: "WalletTypes",
                keyColumn: "Id",
                keyValue: 4L,
                column: "RequiresStatementReconciliation",
                value: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletTypeReconciliationAudits_ChangedByUserId",
                table: "WalletTypeReconciliationAudits",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTypeReconciliationAudits_WalletTypeId",
                table: "WalletTypeReconciliationAudits",
                column: "WalletTypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WalletTypeReconciliationAudits");

            migrationBuilder.DropColumn(
                name: "RequiresStatementReconciliation",
                table: "WalletTypes");

            migrationBuilder.DropColumn(
                name: "StatementWaived",
                table: "ClosingWalletResults");

            migrationBuilder.AlterColumn<long>(
                name: "StatementId",
                table: "ClosingWalletResults",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }
    }
}
