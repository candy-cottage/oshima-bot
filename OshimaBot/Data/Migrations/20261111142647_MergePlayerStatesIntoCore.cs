using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OshimaBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class MergePlayerStatesIntoCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SignInStreak",
                table: "CorePlayers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SignedOn",
                table: "CorePlayers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // 顺序很关键：先把签到进度搬进 CorePlayers，再删 PlayerStates，否则老玩家的签到连续天数与日期会丢。
            // 只搬已有钱包行的用户（正常流程下两者同时创建，孤儿行不存在）。
            migrationBuilder.Sql("""
                UPDATE "CorePlayers"
                SET "SignInStreak" = (SELECT ps."SignInStreak" FROM "PlayerStates" ps WHERE ps."UserId" = "CorePlayers"."UserId"),
                    "SignedOn" = (SELECT ps."SignedOn" FROM "PlayerStates" ps WHERE ps."UserId" = "CorePlayers"."UserId")
                WHERE EXISTS (SELECT 1 FROM "PlayerStates" ps WHERE ps."UserId" = "CorePlayers"."UserId");
                """);

            // CharacterSkills 是死表：技能早已在 CoreCharacters.SnapshotJson 里。
            migrationBuilder.DropTable(
                name: "CharacterSkills");

            migrationBuilder.DropTable(
                name: "PlayerStates");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SignInStreak",
                table: "CorePlayers");

            migrationBuilder.DropColumn(
                name: "SignedOn",
                table: "CorePlayers");

            migrationBuilder.CreateTable(
                name: "CharacterSkills",
                columns: table => new
                {
                    CharacterId = table.Column<long>(type: "INTEGER", nullable: false),
                    SkillId = table.Column<string>(type: "TEXT", nullable: false),
                    Slot = table.Column<int>(type: "INTEGER", nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterSkills", x => new { x.CharacterId, x.SkillId, x.Slot });
                });

            migrationBuilder.CreateTable(
                name: "PlayerStates",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CooperativePoints = table.Column<long>(type: "INTEGER", nullable: false),
                    DinnerClaimedOn = table.Column<string>(type: "TEXT", nullable: false),
                    ExploreTimes = table.Column<int>(type: "INTEGER", nullable: false),
                    ForgePoints = table.Column<long>(type: "INTEGER", nullable: false),
                    HorseRacingPoints = table.Column<long>(type: "INTEGER", nullable: false),
                    LogonOn = table.Column<string>(type: "TEXT", nullable: false),
                    LunchClaimedOn = table.Column<string>(type: "TEXT", nullable: false),
                    SignInStreak = table.Column<int>(type: "INTEGER", nullable: false),
                    SignedOn = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerStates", x => x.UserId);
                });
        }
    }
}
