using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EastSeat.Agenti.Web.Migrations
{
    /// <inheritdoc />
    public partial class MakePerformedByUserIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserAuditLogs_AspNetUsers_PerformedByUserId",
                table: "UserAuditLogs");

            migrationBuilder.AlterColumn<string>(
                name: "PerformedByUserId",
                table: "UserAuditLogs",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(450)",
                oldMaxLength: 450);

            migrationBuilder.AddForeignKey(
                name: "FK_UserAuditLogs_AspNetUsers_PerformedByUserId",
                table: "UserAuditLogs",
                column: "PerformedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserAuditLogs_AspNetUsers_PerformedByUserId",
                table: "UserAuditLogs");

            migrationBuilder.AlterColumn<string>(
                name: "PerformedByUserId",
                table: "UserAuditLogs",
                type: "character varying(450)",
                maxLength: 450,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(450)",
                oldMaxLength: 450,
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_UserAuditLogs_AspNetUsers_PerformedByUserId",
                table: "UserAuditLogs",
                column: "PerformedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
