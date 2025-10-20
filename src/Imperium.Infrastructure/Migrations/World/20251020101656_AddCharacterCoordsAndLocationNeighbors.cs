using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Imperium.Infrastructure.Migrations.World
{
    /// <inheritdoc />
    public partial class AddCharacterCoordsAndLocationNeighbors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NeighborsJson",
                table: "Locations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Characters",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Characters",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NeighborsJson",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Characters");
        }
    }
}
