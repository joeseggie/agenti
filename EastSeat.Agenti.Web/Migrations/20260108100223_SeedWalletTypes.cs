using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace EastSeat.Agenti.Web.Migrations
{
    /// <inheritdoc />
    public partial class SeedWalletTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "WalletTypes",
                columns: new[] { "Id", "CreatedAt", "Description", "IsActive", "IsSystem", "Name", "SupportsDenominations", "Type", "UpdatedAt" },
                values: new object[,]
                {
                    { 1L, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Physical cash in drawer or safe", true, true, "Cash", true, "Cash", null },
                    { 2L, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "MTN Mobile Money float", true, true, "MTN Mobile Money", false, "MobileMoney", null },
                    { 3L, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Airtel Money float", true, true, "Airtel Money", false, "MobileMoney", null },
                    { 4L, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Linked bank account for transfers", true, true, "Bank Account", false, "Bank", null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "WalletTypes",
                keyColumn: "Id",
                keyValue: 1L);

            migrationBuilder.DeleteData(
                table: "WalletTypes",
                keyColumn: "Id",
                keyValue: 2L);

            migrationBuilder.DeleteData(
                table: "WalletTypes",
                keyColumn: "Id",
                keyValue: 3L);

            migrationBuilder.DeleteData(
                table: "WalletTypes",
                keyColumn: "Id",
                keyValue: 4L);
        }
    }
}
