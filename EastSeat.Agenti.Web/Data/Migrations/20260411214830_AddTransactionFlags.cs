using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EastSeat.Agenti.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TransactionFlags",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TransactionId = table.Column<long>(type: "bigint", nullable: false),
                    FlaggedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    FlaggedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    InvestigationNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ResolvedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionFlags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionFlags_AspNetUsers_FlaggedByUserId",
                        column: x => x.FlaggedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionFlags_AspNetUsers_ResolvedByUserId",
                        column: x => x.ResolvedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionFlags_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionFlags_FlaggedByUserId",
                table: "TransactionFlags",
                column: "FlaggedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionFlags_ResolvedByUserId",
                table: "TransactionFlags",
                column: "ResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionFlags_Status_FlaggedAt",
                table: "TransactionFlags",
                columns: new[] { "Status", "FlaggedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionFlags_TransactionId_FlaggedByUserId",
                table: "TransactionFlags",
                columns: new[] { "TransactionId", "FlaggedByUserId" });

            migrationBuilder.CreateIndex(
                name: "UX_TransactionFlags_TransactionId_Active",
                table: "TransactionFlags",
                column: "TransactionId",
                unique: true,
                filter: "\"Status\" IN ('PendingReview', 'UnderInvestigation')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransactionFlags");
        }
    }
}
