using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Imperium.Infrastructure.Migrations.Season
{
    /// <inheritdoc />
    public partial class AddSeasonState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SeasonStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CurrentSeason = table.Column<string>(type: "TEXT", nullable: false),
                    AverageTemperatureC = table.Column<double>(type: "REAL", nullable: false),
                    AveragePrecipitationMm = table.Column<double>(type: "REAL", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DurationTicks = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonStates", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeasonStates");
        }
    }
}
