using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Imperium.Infrastructure.Migrations.Legal
{
    /// <inheritdoc />
    public partial class ReservationsWalletsTreasury : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "MarketOrders",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "MarketOrders");
        }
    }
}
