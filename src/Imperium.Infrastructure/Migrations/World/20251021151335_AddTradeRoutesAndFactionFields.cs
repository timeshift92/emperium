using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Imperium.Infrastructure.Migrations.World
{
    /// <inheritdoc />
    public partial class AddTradeRoutesAndFactionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Army table is created in a separate migration (TempSync)

            // Add faction fields
            migrationBuilder.AddColumn<Guid>(
                name: "ParentFactionId",
                table: "Factions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxPolicyJson",
                table: "Factions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "Factions",
                type: "TEXT",
                nullable: true);

            // Create TradeRoutes table
            migrationBuilder.CreateTable(
                name: "TradeRoutes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromLocationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToLocationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerFactionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Toll = table.Column<decimal>(type: "TEXT", nullable: false),
                    Transport = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeRoutes", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TradeRoutes");

            migrationBuilder.DropColumn(
                name: "ParentFactionId",
                table: "Factions");

            migrationBuilder.DropColumn(
                name: "TaxPolicyJson",
                table: "Factions");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "Factions");
        }
    }
}
