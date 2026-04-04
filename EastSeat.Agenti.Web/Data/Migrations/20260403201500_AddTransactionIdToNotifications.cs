using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EastSeat.Agenti.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionIdToNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                table: "VaultTransactions",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.CreateIndex(
                name: "IX_VaultTransactions_PublicId",
                table: "VaultTransactions",
                column: "PublicId",
                unique: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TransactionId",
                table: "Notifications",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VaultTransactions_PublicId",
                table: "VaultTransactions");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "VaultTransactions");

            migrationBuilder.DropColumn(
                name: "TransactionId",
                table: "Notifications");
        }
    }
}
