using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Imperium.Infrastructure.Migrations.Relationships
{
    /// <inheritdoc />
    public partial class AddHouseholdAndGenealogy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GenealogyRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CharacterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FatherId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MotherId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SpouseIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ChildrenIdsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GenealogyRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Households",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    LocationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    HeadId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MemberIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Wealth = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Households", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GenealogyRecords");

            migrationBuilder.DropTable(
                name: "Households");
        }
    }
}
