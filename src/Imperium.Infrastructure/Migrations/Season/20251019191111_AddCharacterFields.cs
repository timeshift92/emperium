using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Imperium.Infrastructure.Migrations.Season
{
    /// <inheritdoc />
    public partial class AddCharacterFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EssenceJson",
                table: "Characters",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "History",
                table: "Characters",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "Characters",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocationName",
                table: "Characters",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EssenceJson",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "History",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "LocationName",
                table: "Characters");
        }
    }
}
