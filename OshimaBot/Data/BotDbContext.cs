using Microsoft.EntityFrameworkCore;
using OshimaBot.Data.Core;
using OshimaBot.Data.Entities;

namespace OshimaBot.Data;

/// <summary>
/// P0~P2 所需表。P3（clubs / rooms / betting_* 等）按计划不进入首批 Migration。
/// 不规则字段用 TEXT 存 JSON，由应用层序列化，可查询字段一律列式。
/// 外键关系在 S6 仓储层落地时补齐，当前先建表、主键与索引。
/// </summary>
public sealed class BotDbContext(DbContextOptions<BotDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserIdentity> UserIdentities => Set<UserIdentity>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<BindCode> BindCodes => Set<BindCode>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<PlayerState> PlayerStates => Set<PlayerState>();
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<CharacterSkill> CharacterSkills => Set<CharacterSkill>();
    public DbSet<CharacterEquipment> CharacterEquipment => Set<CharacterEquipment>();
    public DbSet<ItemInstance> ItemInstances => Set<ItemInstance>();
    public DbSet<Squad> Squads => Set<Squad>();
    public DbSet<SquadMember> SquadMembers => Set<SquadMember>();
    public DbSet<TrainingSession> TrainingSessions => Set<TrainingSession>();
    public DbSet<DailyQuest> DailyQuests => Set<DailyQuest>();
    public DbSet<SignInRecord> SignInRecords => Set<SignInRecord>();
    public DbSet<ActivityProgress> ActivityProgress => Set<ActivityProgress>();
    public DbSet<ExplorationRun> ExplorationRuns => Set<ExplorationRun>();
    public DbSet<ForgingJob> ForgingJobs => Set<ForgingJob>();
    public DbSet<MailMessage> MailMessages => Set<MailMessage>();
    public DbSet<RenameRequest> RenameRequests => Set<RenameRequest>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StoreGood> StoreGoods => Set<StoreGood>();
    public DbSet<MarketListing> MarketListings => Set<MarketListing>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<TradeItem> TradeItems => Set<TradeItem>();

    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();
    public DbSet<GatewaySession> GatewaySessions => Set<GatewaySession>();
    public DbSet<SimulationRun> SimulationRuns => Set<SimulationRun>();
    public DbSet<SimulationStatistic> SimulationStatistics => Set<SimulationStatistic>();
    public DbSet<AppConfig> AppConfigs => Set<AppConfig>();

    // ---- Core 语义的玩法存储（v3.1 起替代 player_states/characters/item_instances）----
    public DbSet<CorePlayerRecord> CorePlayers => Set<CorePlayerRecord>();
    public DbSet<CoreCharacterRecord> CoreCharacters => Set<CoreCharacterRecord>();
    public DbSet<CoreItemRecord> CoreItems => Set<CoreItemRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ---- 身份与权限 ----
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Handle).IsUnique();
            entity.Property(x => x.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<UserIdentity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Provider, x.Subject }).IsUnique();
            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.UnionSubject);
        });
        modelBuilder.Entity<UserRole>().HasKey(x => new { x.UserId, x.Role });
        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.KeyHash).IsUnique();
        });
        modelBuilder.Entity<BindCode>(entity =>
        {
            entity.HasKey(x => x.Code);
            entity.HasIndex(x => x.UserId);
        });
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ActorUserId, x.CreatedAtUtc });
        });

        // ---- 玩家存档 ----
        modelBuilder.Entity<PlayerState>(entity =>
        {
            entity.HasKey(x => x.UserId);
            entity.Property(x => x.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<Character>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.UserId, x.IsMain });
        });
        modelBuilder.Entity<CharacterSkill>().HasKey(x => new { x.CharacterId, x.SkillId, x.Slot });
        modelBuilder.Entity<CharacterEquipment>().HasKey(x => new { x.CharacterId, x.Slot });
        modelBuilder.Entity<ItemInstance>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.UserId, x.CatalogId });
            entity.Property(x => x.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<Squad>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.UserId);
        });
        modelBuilder.Entity<SquadMember>().HasKey(x => new { x.SquadId, x.Slot });
        modelBuilder.Entity<TrainingSession>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.UserId, x.SettledAtUtc });
        });
        modelBuilder.Entity<DailyQuest>().HasKey(x => new { x.UserId, x.QuestKey, x.QuestDate });
        modelBuilder.Entity<SignInRecord>().HasKey(x => new { x.UserId, x.SignInDate });
        modelBuilder.Entity<ActivityProgress>().HasKey(x => new { x.UserId, x.ActivityKey });
        modelBuilder.Entity<ExplorationRun>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.UserId, x.SettledAtUtc });
        });
        modelBuilder.Entity<ForgingJob>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.UserId, x.SettledAtUtc });
        });
        modelBuilder.Entity<MailMessage>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ReceiverUserId, x.ReadAtUtc });
        });
        modelBuilder.Entity<RenameRequest>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Status, x.CreatedAtUtc });
        });
        modelBuilder.Entity<UserSession>().HasKey(x => x.UserId);

        // ---- 经济与交易 ----
        modelBuilder.Entity<LedgerEntry>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.RefType, x.RefId });
        });
        modelBuilder.Entity<Store>().HasKey(x => x.Key);
        modelBuilder.Entity<StoreGood>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.StoreKey);
        });
        modelBuilder.Entity<MarketListing>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Status, x.CreatedAtUtc });
            entity.HasIndex(x => x.SellerUserId);
        });
        modelBuilder.Entity<Trade>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.SellerUserId);
        });
        modelBuilder.Entity<TradeItem>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TradeId);
        });

        // ---- 系统 / 运行 ----
        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.HasKey(x => new { x.EventId, x.MsgSeq });
            entity.HasIndex(x => x.ReceivedAtUtc);
        });
        modelBuilder.Entity<GatewaySession>().HasKey(x => x.BotAppId);
        modelBuilder.Entity<SimulationRun>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.UserId, x.StartedAtUtc });
        });
        modelBuilder.Entity<SimulationStatistic>().HasKey(x => new { x.Scope, x.CatalogId });
        modelBuilder.Entity<AppConfig>().HasKey(x => x.Key);

        // ---- Core 语义的玩法存储 ----
        modelBuilder.Entity<CorePlayerRecord>(entity =>
        {
            entity.HasKey(x => x.UserId);
            entity.Property(x => x.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<CoreCharacterRecord>(entity =>
        {
            entity.HasKey(x => x.Guid);
            entity.HasIndex(x => new { x.UserId, x.Level });
            entity.HasIndex(x => new { x.UserId, x.Promotion });
            entity.Property(x => x.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<CoreItemRecord>(entity =>
        {
            entity.HasKey(x => x.Guid);
            entity.HasIndex(x => new { x.UserId, x.Quality });
            entity.HasIndex(x => new { x.EscrowUserId, x.Quality });
            entity.HasIndex(x => new { x.CatalogId });
            entity.Property(x => x.Version).IsConcurrencyToken();
        });
    }
}
