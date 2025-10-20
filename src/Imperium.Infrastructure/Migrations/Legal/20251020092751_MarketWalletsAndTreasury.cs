using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Imperium.Infrastructure.Migrations.Legal
{
    /// <inheritdoc />
    public partial class MarketWalletsAndTreasury : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ReservedFunds",
                table: "MarketOrders",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReservedQty",
                table: "MarketOrders",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Treasury",
                table: "Locations",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReservedFunds",
                table: "MarketOrders");

            migrationBuilder.DropColumn(
                name: "ReservedQty",
                table: "MarketOrders");

            migrationBuilder.DropColumn(
                name: "Treasury",
                table: "Locations");
        }
    }
}
