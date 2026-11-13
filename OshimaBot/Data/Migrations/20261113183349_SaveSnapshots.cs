using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OshimaBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class SaveSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SaveSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    GameDay = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: false),
                    Credits = table.Column<double>(type: "REAL", nullable: false),
                    Materials = table.Column<double>(type: "REAL", nullable: false),
                    CharacterCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaveSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SaveSnapshots_UserId_CreatedAtUtc",
                table: "SaveSnapshots",
                columns: new[] { "UserId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SaveSnapshots");
        }
    }
}
