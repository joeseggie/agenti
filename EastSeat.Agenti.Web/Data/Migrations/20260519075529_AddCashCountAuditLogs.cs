using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EastSeat.Agenti.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCashCountAuditLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CashCountAuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CashCountId = table.Column<long>(type: "bigint", nullable: false),
                    CashSessionId = table.Column<long>(type: "bigint", nullable: false),
                    AgentId = table.Column<long>(type: "bigint", nullable: false),
                    IsOpening = table.Column<bool>(type: "boolean", nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PreviousStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    NewStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PerformedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    PerformedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashCountAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashCountAuditLogs_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashCountAuditLogs_AspNetUsers_PerformedByUserId",
                        column: x => x.PerformedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashCountAuditLogs_CashCounts_CashCountId",
                        column: x => x.CashCountId,
                        principalTable: "CashCounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CashCountAuditLogs_CashSessions_CashSessionId",
                        column: x => x.CashSessionId,
                        principalTable: "CashSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashCountAuditLogs_AgentId",
                table: "CashCountAuditLogs",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_CashCountAuditLogs_CashCountId_PerformedAt",
                table: "CashCountAuditLogs",
                columns: new[] { "CashCountId", "PerformedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CashCountAuditLogs_CashSessionId_PerformedAt",
                table: "CashCountAuditLogs",
                columns: new[] { "CashSessionId", "PerformedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CashCountAuditLogs_PerformedByUserId",
                table: "CashCountAuditLogs",
                column: "PerformedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashCountAuditLogs");
        }
    }
}
