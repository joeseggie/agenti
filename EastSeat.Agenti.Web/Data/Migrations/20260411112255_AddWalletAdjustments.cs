using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EastSeat.Agenti.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WalletAdjustments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CashSessionId = table.Column<long>(type: "bigint", nullable: false),
                    WalletId = table.Column<long>(type: "bigint", nullable: false),
                    AgentId = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RecordedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WalletAdjustments_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WalletAdjustments_AspNetUsers_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WalletAdjustments_CashSessions_CashSessionId",
                        column: x => x.CashSessionId,
                        principalTable: "CashSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WalletAdjustments_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WalletAdjustments_AgentId",
                table: "WalletAdjustments",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletAdjustments_CashSessionId_AgentId",
                table: "WalletAdjustments",
                columns: new[] { "CashSessionId", "AgentId" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletAdjustments_CashSessionId_WalletId",
                table: "WalletAdjustments",
                columns: new[] { "CashSessionId", "WalletId" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletAdjustments_RecordedByUserId",
                table: "WalletAdjustments",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletAdjustments_WalletId",
                table: "WalletAdjustments",
                column: "WalletId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WalletAdjustments");
        }
    }
}
