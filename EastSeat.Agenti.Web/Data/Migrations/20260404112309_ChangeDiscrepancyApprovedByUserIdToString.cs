using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EastSeat.Agenti.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChangeDiscrepancyApprovedByUserIdToString : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Add AgentId to CashCounts BEFORE dropping it from CashSessions
            // so we can copy the data across.
            migrationBuilder.AddColumn<long>(
                name: "AgentId",
                table: "CashCounts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Step 2: Populate CashCounts.AgentId from CashSessions.AgentId
            migrationBuilder.Sql(
                """
                UPDATE "CashCounts" SET "AgentId" = cs."AgentId"
                FROM "CashSessions" cs
                WHERE "CashCounts"."CashSessionId" = cs."Id"
                """);

            // Step 3: Now safe to drop AgentId from CashSessions
            migrationBuilder.DropForeignKey(
                name: "FK_CashSessions_Agents_AgentId",
                table: "CashSessions");

            migrationBuilder.DropIndex(
                name: "IX_CashSessions_AgentId_SessionDate",
                table: "CashSessions");

            migrationBuilder.DropIndex(
                name: "IX_CashCounts_CashSessionId_IsOpening",
                table: "CashCounts");

            migrationBuilder.DropColumn(
                name: "AgentId",
                table: "CashSessions");

            migrationBuilder.AlterColumn<string>(
                name: "Message",
                table: "Notifications",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AddColumn<string>(
                name: "LinkUrl",
                table: "Notifications",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Notifications",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Notifications",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ApprovedByUserId",
                table: "Discrepancies",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "BranchId",
                table: "CashSessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedByUserId",
                table: "CashCounts",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CountDate",
                table: "CashCounts",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<string>(
                name: "Explanation",
                table: "CashCounts",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RejectedAt",
                table: "CashCounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectedByUserId",
                table: "CashCounts",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "CashCounts",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "CashCounts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Discrepancies_ApprovedByUserId",
                table: "Discrepancies",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_BranchId_SessionDate",
                table: "CashSessions",
                columns: new[] { "BranchId", "SessionDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashCounts_AgentId_Status",
                table: "CashCounts",
                columns: new[] { "AgentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CashCounts_ApprovedByUserId",
                table: "CashCounts",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CashCounts_CashSessionId_AgentId_IsOpening",
                table: "CashCounts",
                columns: new[] { "CashSessionId", "AgentId", "IsOpening" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashCounts_RejectedByUserId",
                table: "CashCounts",
                column: "RejectedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_CashCounts_Agents_AgentId",
                table: "CashCounts",
                column: "AgentId",
                principalTable: "Agents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CashCounts_AspNetUsers_ApprovedByUserId",
                table: "CashCounts",
                column: "ApprovedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CashCounts_AspNetUsers_RejectedByUserId",
                table: "CashCounts",
                column: "RejectedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CashSessions_Branches_BranchId",
                table: "CashSessions",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Discrepancies_AspNetUsers_ApprovedByUserId",
                table: "Discrepancies",
                column: "ApprovedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CashCounts_Agents_AgentId",
                table: "CashCounts");

            migrationBuilder.DropForeignKey(
                name: "FK_CashCounts_AspNetUsers_ApprovedByUserId",
                table: "CashCounts");

            migrationBuilder.DropForeignKey(
                name: "FK_CashCounts_AspNetUsers_RejectedByUserId",
                table: "CashCounts");

            migrationBuilder.DropForeignKey(
                name: "FK_CashSessions_Branches_BranchId",
                table: "CashSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_Discrepancies_AspNetUsers_ApprovedByUserId",
                table: "Discrepancies");

            migrationBuilder.DropIndex(
                name: "IX_Discrepancies_ApprovedByUserId",
                table: "Discrepancies");

            migrationBuilder.DropIndex(
                name: "IX_CashSessions_BranchId_SessionDate",
                table: "CashSessions");

            migrationBuilder.DropIndex(
                name: "IX_CashCounts_AgentId_Status",
                table: "CashCounts");

            migrationBuilder.DropIndex(
                name: "IX_CashCounts_ApprovedByUserId",
                table: "CashCounts");

            migrationBuilder.DropIndex(
                name: "IX_CashCounts_CashSessionId_AgentId_IsOpening",
                table: "CashCounts");

            migrationBuilder.DropIndex(
                name: "IX_CashCounts_RejectedByUserId",
                table: "CashCounts");

            migrationBuilder.DropColumn(
                name: "LinkUrl",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "AgentId",
                table: "CashCounts");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "CashCounts");

            migrationBuilder.DropColumn(
                name: "CountDate",
                table: "CashCounts");

            migrationBuilder.DropColumn(
                name: "Explanation",
                table: "CashCounts");

            migrationBuilder.DropColumn(
                name: "RejectedAt",
                table: "CashCounts");

            migrationBuilder.DropColumn(
                name: "RejectedByUserId",
                table: "CashCounts");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "CashCounts");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "CashCounts");

            migrationBuilder.AlterColumn<string>(
                name: "Message",
                table: "Notifications",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<long>(
                name: "ApprovedByUserId",
                table: "Discrepancies",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(450)",
                oldMaxLength: 450,
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "BranchId",
                table: "CashSessions",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "AgentId",
                table: "CashSessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_AgentId_SessionDate",
                table: "CashSessions",
                columns: new[] { "AgentId", "SessionDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashCounts_CashSessionId_IsOpening",
                table: "CashCounts",
                columns: new[] { "CashSessionId", "IsOpening" });

            migrationBuilder.AddForeignKey(
                name: "FK_CashSessions_Agents_AgentId",
                table: "CashSessions",
                column: "AgentId",
                principalTable: "Agents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
