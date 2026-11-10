using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OshimaBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class WalletToCorePlayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 老库升级：先把旧余额搬进 CorePlayers，再删列，避免玩家资产被清零。
            migrationBuilder.Sql("""
                INSERT INTO "CorePlayers" ("UserId", "Credits", "Materials", "MainCharacterGuid", "UpdatedAtUtc", "Version")
                SELECT ps."UserId", ps."Credits", ps."Diamonds", NULL, ps."UpdatedAtUtc", 0
                FROM "PlayerStates" ps
                WHERE NOT EXISTS (SELECT 1 FROM "CorePlayers" cp WHERE cp."UserId" = ps."UserId");
                """);

            migrationBuilder.DropColumn(
                name: "Credits",
                table: "PlayerStates");

            migrationBuilder.DropColumn(
                name: "Diamonds",
                table: "PlayerStates");

            migrationBuilder.AlterColumn<double>(
                name: "Delta",
                table: "LedgerEntries",
                type: "REAL",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<double>(
                name: "BalanceAfter",
                table: "LedgerEntries",
                type: "REAL",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Credits",
                table: "PlayerStates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "Diamonds",
                table: "PlayerStates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AlterColumn<long>(
                name: "Delta",
                table: "LedgerEntries",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "REAL");

            migrationBuilder.AlterColumn<long>(
                name: "BalanceAfter",
                table: "LedgerEntries",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "REAL");
        }
    }
}
