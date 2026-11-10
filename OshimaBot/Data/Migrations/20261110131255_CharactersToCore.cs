using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OshimaBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class CharactersToCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Characters");

            // 旧 Characters 行只有 catalog_id + 等级/经验，无法还原成 Core 实体快照，
            // 因此角色数据重建；清空后再把 Guid 改成自增主键，避免 LocalId 全为 0 撞主键。
            migrationBuilder.Sql("DELETE FROM \"CoreCharacters\";");
            migrationBuilder.Sql("DELETE FROM \"TrainingSessions\";");
            migrationBuilder.Sql("DELETE FROM \"SquadMembers\";");
            migrationBuilder.Sql("DELETE FROM \"CharacterEquipment\";");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CoreCharacters",
                table: "CoreCharacters");

            migrationBuilder.AddColumn<long>(
                name: "LocalId",
                table: "CoreCharacters",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_CoreCharacters",
                table: "CoreCharacters",
                column: "LocalId");

            migrationBuilder.CreateIndex(
                name: "IX_CoreCharacters_Guid",
                table: "CoreCharacters",
                column: "Guid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_CoreCharacters",
                table: "CoreCharacters");

            migrationBuilder.DropIndex(
                name: "IX_CoreCharacters_Guid",
                table: "CoreCharacters");

            migrationBuilder.DropColumn(
                name: "LocalId",
                table: "CoreCharacters");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CoreCharacters",
                table: "CoreCharacters",
                column: "Guid");

            migrationBuilder.CreateTable(
                name: "Characters",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AttrsJson = table.Column<string>(type: "TEXT", nullable: false),
                    BreakLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    CatalogId = table.Column<string>(type: "TEXT", nullable: false),
                    CatalogVersion = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Exp = table.Column<long>(type: "INTEGER", nullable: false),
                    IsMain = table.Column<bool>(type: "INTEGER", nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Characters", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Characters_UserId_IsMain",
                table: "Characters",
                columns: new[] { "UserId", "IsMain" });
        }
    }
}
