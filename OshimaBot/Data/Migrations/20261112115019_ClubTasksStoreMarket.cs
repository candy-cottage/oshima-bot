using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OshimaBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClubTasksStoreMarket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClubMarketListings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClubId = table.Column<long>(type: "INTEGER", nullable: false),
                    SellerUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    ItemInstanceId = table.Column<long>(type: "INTEGER", nullable: false),
                    Price = table.Column<long>(type: "INTEGER", nullable: false),
                    Fee = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubMarketListings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClubTaskProgresses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClubId = table.Column<long>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Period = table.Column<string>(type: "TEXT", nullable: false),
                    Progress = table.Column<int>(type: "INTEGER", nullable: false),
                    ClaimedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubTaskProgresses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClubMarketListings_ClubId_Status",
                table: "ClubMarketListings",
                columns: new[] { "ClubId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ClubMarketListings_SellerUserId",
                table: "ClubMarketListings",
                column: "SellerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubTaskProgresses_ClubId_Period_Key",
                table: "ClubTaskProgresses",
                columns: new[] { "ClubId", "Period", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClubMarketListings");

            migrationBuilder.DropTable(
                name: "ClubTaskProgresses");
        }
    }
}
