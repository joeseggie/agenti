using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EastSeat.Agenti.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletAdjustmentApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                table: "WalletAdjustments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedByUserId",
                table: "WalletAdjustments",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RejectedAt",
                table: "WalletAdjustments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectedByUserId",
                table: "WalletAdjustments",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "WalletAdjustments",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "WalletAdjustments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_WalletAdjustments_ApprovedByUserId",
                table: "WalletAdjustments",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletAdjustments_RejectedByUserId",
                table: "WalletAdjustments",
                column: "RejectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletAdjustments_Status_CashSessionId",
                table: "WalletAdjustments",
                columns: new[] { "Status", "CashSessionId" });

            migrationBuilder.AddForeignKey(
                name: "FK_WalletAdjustments_AspNetUsers_ApprovedByUserId",
                table: "WalletAdjustments",
                column: "ApprovedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WalletAdjustments_AspNetUsers_RejectedByUserId",
                table: "WalletAdjustments",
                column: "RejectedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WalletAdjustments_AspNetUsers_ApprovedByUserId",
                table: "WalletAdjustments");

            migrationBuilder.DropForeignKey(
                name: "FK_WalletAdjustments_AspNetUsers_RejectedByUserId",
                table: "WalletAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_WalletAdjustments_ApprovedByUserId",
                table: "WalletAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_WalletAdjustments_RejectedByUserId",
                table: "WalletAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_WalletAdjustments_Status_CashSessionId",
                table: "WalletAdjustments");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "WalletAdjustments");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "WalletAdjustments");

            migrationBuilder.DropColumn(
                name: "RejectedAt",
                table: "WalletAdjustments");

            migrationBuilder.DropColumn(
                name: "RejectedByUserId",
                table: "WalletAdjustments");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "WalletAdjustments");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "WalletAdjustments");
        }
    }
}
