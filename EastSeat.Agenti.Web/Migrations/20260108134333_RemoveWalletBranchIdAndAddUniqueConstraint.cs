using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EastSeat.Agenti.Web.Migrations
{
    /// <inheritdoc />
    public partial class RemoveWalletBranchIdAndAddUniqueConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Wallets_AgentId_BranchId",
                table: "Wallets");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Wallets");

            migrationBuilder.AlterColumn<long>(
                name: "AgentId",
                table: "Wallets",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_AgentId_WalletTypeId",
                table: "Wallets",
                columns: new[] { "AgentId", "WalletTypeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Wallets_AgentId_WalletTypeId",
                table: "Wallets");

            migrationBuilder.AlterColumn<long>(
                name: "AgentId",
                table: "Wallets",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "BranchId",
                table: "Wallets",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_AgentId_BranchId",
                table: "Wallets",
                columns: new[] { "AgentId", "BranchId" });
        }
    }
}
