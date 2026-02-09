using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EastSeat.Agenti.Web.Migrations
{
    /// <inheritdoc />
    public partial class MoveAgentProfileToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Agents_AgentId",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_AgentId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "Agents");

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "AspNetUsers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "AspNetUsers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddForeignKey(
                name: "FK_Agents_AspNetUsers_UserId",
                table: "Agents",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Agents_AspNetUsers_UserId",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "AspNetUsers");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Agents",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "Agents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "Agents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "Agents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_AgentId",
                table: "AspNetUsers",
                column: "AgentId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Agents_AgentId",
                table: "AspNetUsers",
                column: "AgentId",
                principalTable: "Agents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
