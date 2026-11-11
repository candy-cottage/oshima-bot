using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OshimaBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class StoreCatalogAndPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 旧的「铎京补给站」（Key=system）是自造货架，已被 7 个旧版商店取代。
            migrationBuilder.Sql("DELETE FROM \"StoreGoods\" WHERE \"StoreKey\" = 'system';");
            migrationBuilder.Sql("DELETE FROM \"Stores\" WHERE \"Key\" = 'system';");
            // 旧语义 Quota=-1 表示不限购，新语义用 0；统一一下。
            migrationBuilder.Sql("UPDATE \"StoreGoods\" SET \"Quota\" = 0 WHERE \"Quota\" < 0;");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "StoreGoods",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "OwnerUserId",
                table: "StoreGoods",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "Slot",
                table: "StoreGoods",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SoldCount",
                table: "StoreGoods",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "UsersBuyCountJson",
                table: "StoreGoods",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "CooperativePoints",
                table: "CorePlayers",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "ForgePoints",
                table: "CorePlayers",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "HorseRacingPoints",
                table: "CorePlayers",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "LastStoreKey",
                table: "CorePlayers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Name",
                table: "StoreGoods");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "StoreGoods");

            migrationBuilder.DropColumn(
                name: "Slot",
                table: "StoreGoods");

            migrationBuilder.DropColumn(
                name: "SoldCount",
                table: "StoreGoods");

            migrationBuilder.DropColumn(
                name: "UsersBuyCountJson",
                table: "StoreGoods");

            migrationBuilder.DropColumn(
                name: "CooperativePoints",
                table: "CorePlayers");

            migrationBuilder.DropColumn(
                name: "ForgePoints",
                table: "CorePlayers");

            migrationBuilder.DropColumn(
                name: "HorseRacingPoints",
                table: "CorePlayers");

            migrationBuilder.DropColumn(
                name: "LastStoreKey",
                table: "CorePlayers");
        }
    }
}
