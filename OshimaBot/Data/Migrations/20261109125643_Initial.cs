using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OshimaBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityProgress",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    ActivityKey = table.Column<string>(type: "TEXT", nullable: false),
                    ProgressJson = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimedJson = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityProgress", x => new { x.UserId, x.ActivityKey });
                });

            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<long>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    KeyHash = table.Column<string>(type: "TEXT", nullable: false),
                    ScopesJson = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppConfigs",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    ValueJson = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppConfigs", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActorUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    Target = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BindCodes",
                columns: table => new
                {
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BindCodes", x => x.Code);
                });

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
                name: "Characters",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CatalogId = table.Column<string>(type: "TEXT", nullable: false),
                    CatalogVersion = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    Exp = table.Column<long>(type: "INTEGER", nullable: false),
                    BreakLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    AttrsJson = table.Column<string>(type: "TEXT", nullable: false),
                    IsMain = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Characters", x => x.Id);
                });

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
                name: "DailyQuests",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    QuestKey = table.Column<string>(type: "TEXT", nullable: false),
                    QuestDate = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ProgressJson = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyQuests", x => new { x.UserId, x.QuestKey, x.QuestDate });
                });

            migrationBuilder.CreateTable(
                name: "ExplorationRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    RegionKey = table.Column<string>(type: "TEXT", nullable: false),
                    StateJson = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SettledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExplorationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ForgingJobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    RecipeKey = table.Column<string>(type: "TEXT", nullable: false),
                    StateJson = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SettledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForgingJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GatewaySessions",
                columns: table => new
                {
                    BotAppId = table.Column<string>(type: "TEXT", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", nullable: false),
                    LastSeq = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatewaySessions", x => x.BotAppId);
                });

            migrationBuilder.CreateTable(
                name: "ItemInstances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CatalogId = table.Column<string>(type: "TEXT", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Quality = table.Column<int>(type: "INTEGER", nullable: false),
                    Locked = table.Column<bool>(type: "INTEGER", nullable: false),
                    Bound = table.Column<bool>(type: "INTEGER", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    AcquiredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemInstances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LedgerEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", nullable: false),
                    Delta = table.Column<long>(type: "INTEGER", nullable: false),
                    BalanceAfter = table.Column<long>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    RefType = table.Column<string>(type: "TEXT", nullable: false),
                    RefId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MailMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SenderUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    ReceiverUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MarketListings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
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
                    table.PrimaryKey("PK_MarketListings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerStates",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Credits = table.Column<long>(type: "INTEGER", nullable: false),
                    Diamonds = table.Column<long>(type: "INTEGER", nullable: false),
                    Exp = table.Column<long>(type: "INTEGER", nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    SignInStreak = table.Column<int>(type: "INTEGER", nullable: false),
                    SignedOn = table.Column<string>(type: "TEXT", nullable: false),
                    LogonOn = table.Column<string>(type: "TEXT", nullable: false),
                    LunchClaimedOn = table.Column<string>(type: "TEXT", nullable: false),
                    DinnerClaimedOn = table.Column<string>(type: "TEXT", nullable: false),
                    ExploreTimes = table.Column<int>(type: "INTEGER", nullable: false),
                    HorseRacingPoints = table.Column<long>(type: "INTEGER", nullable: false),
                    CooperativePoints = table.Column<long>(type: "INTEGER", nullable: false),
                    ForgePoints = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerStates", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedEvents",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "TEXT", nullable: false),
                    MsgSeq = table.Column<int>(type: "INTEGER", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedEvents", x => new { x.EventId, x.MsgSeq });
                });

            migrationBuilder.CreateTable(
                name: "RenameRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetType = table.Column<string>(type: "TEXT", nullable: false),
                    TargetId = table.Column<string>(type: "TEXT", nullable: false),
                    RequestedName = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RenameRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SignInRecords",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    SignInDate = table.Column<string>(type: "TEXT", nullable: false),
                    RewardJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignInRecords", x => new { x.UserId, x.SignInDate });
                });

            migrationBuilder.CreateTable(
                name: "SimulationRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<long>(type: "INTEGER", nullable: true),
                    Seed = table.Column<string>(type: "TEXT", nullable: false),
                    OptionsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: false),
                    RoundsJson = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimulationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SimulationStatistics",
                columns: table => new
                {
                    Scope = table.Column<string>(type: "TEXT", nullable: false),
                    CatalogId = table.Column<string>(type: "TEXT", nullable: false),
                    StatsJson = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimulationStatistics", x => new { x.Scope, x.CatalogId });
                });

            migrationBuilder.CreateTable(
                name: "SquadMembers",
                columns: table => new
                {
                    SquadId = table.Column<long>(type: "INTEGER", nullable: false),
                    Slot = table.Column<int>(type: "INTEGER", nullable: false),
                    CharacterId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SquadMembers", x => new { x.SquadId, x.Slot });
                });

            migrationBuilder.CreateTable(
                name: "Squads",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Squads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoreGoods",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StoreKey = table.Column<string>(type: "TEXT", nullable: false),
                    ItemCatalogId = table.Column<string>(type: "TEXT", nullable: false),
                    PriceCurrency = table.Column<string>(type: "TEXT", nullable: false),
                    Price = table.Column<long>(type: "INTEGER", nullable: false),
                    Stock = table.Column<int>(type: "INTEGER", nullable: false),
                    Quota = table.Column<int>(type: "INTEGER", nullable: false),
                    RefreshAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreGoods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stores",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    RefreshAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConfigJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stores", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "TradeItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TradeId = table.Column<long>(type: "INTEGER", nullable: false),
                    FromUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    ItemInstanceId = table.Column<long>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Price = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Trades",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BuyerUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    SellerUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trades", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrainingSessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CharacterId = table.Column<long>(type: "INTEGER", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SettledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserIdentities",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", nullable: false),
                    UnionSubject = table.Column<string>(type: "TEXT", nullable: true),
                    Scene = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserIdentities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.Role });
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Handle = table.Column<string>(type: "TEXT", nullable: false),
                    Nickname = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserSessions",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CurrentRegion = table.Column<string>(type: "TEXT", nullable: false),
                    PendingActionJson = table.Column<string>(type: "TEXT", nullable: false),
                    PendingExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PageCursor = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSessions", x => x.UserId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_KeyHash",
                table: "ApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ActorUserId_CreatedAtUtc",
                table: "AuditLogs",
                columns: new[] { "ActorUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BindCodes_UserId",
                table: "BindCodes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_UserId_IsMain",
                table: "Characters",
                columns: new[] { "UserId", "IsMain" });

            migrationBuilder.CreateIndex(
                name: "IX_ExplorationRuns_UserId_SettledAtUtc",
                table: "ExplorationRuns",
                columns: new[] { "UserId", "SettledAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ForgingJobs_UserId_SettledAtUtc",
                table: "ForgingJobs",
                columns: new[] { "UserId", "SettledAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemInstances_UserId_CatalogId",
                table: "ItemInstances",
                columns: new[] { "UserId", "CatalogId" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_RefType_RefId",
                table: "LedgerEntries",
                columns: new[] { "RefType", "RefId" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_UserId_CreatedAtUtc",
                table: "LedgerEntries",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MailMessages_ReceiverUserId_ReadAtUtc",
                table: "MailMessages",
                columns: new[] { "ReceiverUserId", "ReadAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketListings_SellerUserId",
                table: "MarketListings",
                column: "SellerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketListings_Status_CreatedAtUtc",
                table: "MarketListings",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedEvents_ReceivedAtUtc",
                table: "ProcessedEvents",
                column: "ReceivedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RenameRequests_Status_CreatedAtUtc",
                table: "RenameRequests",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SimulationRuns_UserId_StartedAtUtc",
                table: "SimulationRuns",
                columns: new[] { "UserId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Squads_UserId",
                table: "Squads",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreGoods_StoreKey",
                table: "StoreGoods",
                column: "StoreKey");

            migrationBuilder.CreateIndex(
                name: "IX_TradeItems_TradeId",
                table: "TradeItems",
                column: "TradeId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_SellerUserId",
                table: "Trades",
                column: "SellerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingSessions_UserId_SettledAtUtc",
                table: "TrainingSessions",
                columns: new[] { "UserId", "SettledAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserIdentities_Provider_Subject",
                table: "UserIdentities",
                columns: new[] { "Provider", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserIdentities_UnionSubject",
                table: "UserIdentities",
                column: "UnionSubject");

            migrationBuilder.CreateIndex(
                name: "IX_UserIdentities_UserId",
                table: "UserIdentities",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Handle",
                table: "Users",
                column: "Handle",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityProgress");

            migrationBuilder.DropTable(
                name: "ApiKeys");

            migrationBuilder.DropTable(
                name: "AppConfigs");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "BindCodes");

            migrationBuilder.DropTable(
                name: "CharacterEquipment");

            migrationBuilder.DropTable(
                name: "Characters");

            migrationBuilder.DropTable(
                name: "CharacterSkills");

            migrationBuilder.DropTable(
                name: "DailyQuests");

            migrationBuilder.DropTable(
                name: "ExplorationRuns");

            migrationBuilder.DropTable(
                name: "ForgingJobs");

            migrationBuilder.DropTable(
                name: "GatewaySessions");

            migrationBuilder.DropTable(
                name: "ItemInstances");

            migrationBuilder.DropTable(
                name: "LedgerEntries");

            migrationBuilder.DropTable(
                name: "MailMessages");

            migrationBuilder.DropTable(
                name: "MarketListings");

            migrationBuilder.DropTable(
                name: "PlayerStates");

            migrationBuilder.DropTable(
                name: "ProcessedEvents");

            migrationBuilder.DropTable(
                name: "RenameRequests");

            migrationBuilder.DropTable(
                name: "SignInRecords");

            migrationBuilder.DropTable(
                name: "SimulationRuns");

            migrationBuilder.DropTable(
                name: "SimulationStatistics");

            migrationBuilder.DropTable(
                name: "SquadMembers");

            migrationBuilder.DropTable(
                name: "Squads");

            migrationBuilder.DropTable(
                name: "StoreGoods");

            migrationBuilder.DropTable(
                name: "Stores");

            migrationBuilder.DropTable(
                name: "TradeItems");

            migrationBuilder.DropTable(
                name: "Trades");

            migrationBuilder.DropTable(
                name: "TrainingSessions");

            migrationBuilder.DropTable(
                name: "UserIdentities");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "UserSessions");
        }
    }
}
