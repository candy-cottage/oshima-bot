using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OshimaBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class ItemsToCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharacterEquipment");

            migrationBuilder.DropTable(
                name: "ItemInstances");

            // 旧 ItemInstances 只有目录 + 品质，无法还原成 Core Item 快照（属性/技能/装备槽都在快照里），
            // 因此物品数据重建；清空后再把 Guid 改成自增主键，避免 LocalId 全为 0 撞主键。
            migrationBuilder.Sql("DELETE FROM \"CoreItems\";");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CoreItems",
                table: "CoreItems");

            migrationBuilder.AddColumn<long>(
                name: "LocalId",
                table: "CoreItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBound",
                table: "CoreItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ItemType",
                table: "CoreItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_CoreItems",
                table: "CoreItems",
                column: "LocalId");

            migrationBuilder.CreateIndex(
                name: "IX_CoreItems_Guid",
                table: "CoreItems",
                column: "Guid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_CoreItems",
                table: "CoreItems");

            migrationBuilder.DropIndex(
                name: "IX_CoreItems_Guid",
                table: "CoreItems");

            migrationBuilder.DropColumn(
                name: "LocalId",
                table: "CoreItems");

            migrationBuilder.DropColumn(
                name: "IsBound",
                table: "CoreItems");

            migrationBuilder.DropColumn(
                name: "ItemType",
                table: "CoreItems");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CoreItems",
                table: "CoreItems",
                column: "Guid");

            migrationBuilder.CreateTable(
                name: "CharacterEquipment",
                columns: table => new
                {
                    CharacterId = table.Column<long>(type: "INTEGER", nullable: false),
                    Slot = table.Column<string>(type: "TEXT", nullable: false),
                    ItemInstanceId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterEquipment", x => new { x.CharacterId, x.Slot });
                });

            migrationBuilder.CreateTable(
                name: "ItemInstances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AcquiredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Bound = table.Column<bool>(type: "INTEGER", nullable: false),
                    CatalogId = table.Column<string>(type: "TEXT", nullable: false),
                    Locked = table.Column<bool>(type: "INTEGER", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    Quality = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemInstances", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemInstances_UserId_CatalogId",
                table: "ItemInstances",
                columns: new[] { "UserId", "CatalogId" });
        }
    }
}
