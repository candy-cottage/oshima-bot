using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OshimaBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class CoreModelAlignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CoreCharacters",
                columns: table => new
                {
                    Guid = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CatalogId = table.Column<string>(type: "TEXT", nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    Exp = table.Column<double>(type: "REAL", nullable: false),
                    ExLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    LevelBreak = table.Column<int>(type: "INTEGER", nullable: false),
                    Promotion = table.Column<int>(type: "INTEGER", nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoreCharacters", x => x.Guid);
                });

            migrationBuilder.CreateTable(
                name: "CoreItems",
                columns: table => new
                {
                    Guid = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    EscrowUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CatalogId = table.Column<string>(type: "TEXT", nullable: false),
                    Quality = table.Column<int>(type: "INTEGER", nullable: false),
                    Price = table.Column<double>(type: "REAL", nullable: false),
                    IsLock = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsTradable = table.Column<bool>(type: "INTEGER", nullable: false),
                    EquipSlotType = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoreItems", x => x.Guid);
                });

            migrationBuilder.CreateTable(
                name: "CorePlayers",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Credits = table.Column<double>(type: "REAL", nullable: false),
                    Materials = table.Column<double>(type: "REAL", nullable: false),
                    MainCharacterGuid = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorePlayers", x => x.UserId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoreCharacters_UserId_Level",
                table: "CoreCharacters",
                columns: new[] { "UserId", "Level" });

            migrationBuilder.CreateIndex(
                name: "IX_CoreCharacters_UserId_Promotion",
                table: "CoreCharacters",
                columns: new[] { "UserId", "Promotion" });

            migrationBuilder.CreateIndex(
                name: "IX_CoreItems_CatalogId",
                table: "CoreItems",
                column: "CatalogId");

            migrationBuilder.CreateIndex(
                name: "IX_CoreItems_EscrowUserId_Quality",
                table: "CoreItems",
                columns: new[] { "EscrowUserId", "Quality" });

            migrationBuilder.CreateIndex(
                name: "IX_CoreItems_UserId_Quality",
                table: "CoreItems",
                columns: new[] { "UserId", "Quality" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoreCharacters");

            migrationBuilder.DropTable(
                name: "CoreItems");

            migrationBuilder.DropTable(
                name: "CorePlayers");
        }
    }
}
