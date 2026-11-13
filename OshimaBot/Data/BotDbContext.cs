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

    // ---- 人物存档快照（旧版的「还原存档」是空壳，这里做成真的）----
    public DbSet<SaveSnapshot> SaveSnapshots => Set<SaveSnapshot>();

    // ---- 社团（M6.2）----
    public DbSet<Club> Clubs => Set<Club>();
    public DbSet<ClubMember> ClubMembers => Set<ClubMember>();
    public DbSet<ClubApplication> ClubApplications => Set<ClubApplication>();
    public DbSet<ClubInvite> ClubInvites => Set<ClubInvite>();
    public DbSet<ClubTaskProgress> ClubTaskProgresses => Set<ClubTaskProgress>();
    public DbSet<ClubMarketListing> ClubMarketListings => Set<ClubMarketListing>();

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

        // ---- 玩家存档（进度已并入 CorePlayers，这里只剩队伍/训练等玩法表）----
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

        // ---- 社团 ----
        modelBuilder.Entity<Club>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Name);
            entity.Property(x => x.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<ClubMember>(entity =>
        {
            entity.HasKey(x => x.Id);
            // 一个人只能待在一个社团：这条唯一索引比业务层的检查更硬。
            entity.HasIndex(x => x.UserId).IsUnique();
            entity.HasIndex(x => x.ClubId);
        });
        modelBuilder.Entity<ClubApplication>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ClubId, x.UserId }).IsUnique();
        });
        modelBuilder.Entity<ClubInvite>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ClubId, x.UserId }).IsUnique();
            entity.HasIndex(x => x.UserId);
        });
        modelBuilder.Entity<ClubTaskProgress>(entity =>
        {
            entity.HasKey(x => x.Id);
            // 一个社团在一个周期里，每个任务只有一行进度。
            entity.HasIndex(x => new { x.ClubId, x.Period, x.Key }).IsUnique();
        });
        modelBuilder.Entity<ClubMarketListing>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ClubId, x.Status });
            entity.HasIndex(x => x.SellerUserId);
        });

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
            // 自增本地键用于关联表外键；业务身份是 Guid（唯一索引）。
            entity.HasKey(x => x.LocalId);
            entity.HasIndex(x => x.Guid).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.Level });
            entity.HasIndex(x => new { x.UserId, x.Promotion });
            entity.Property(x => x.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<SaveSnapshot>(entity =>
        {
            entity.HasKey(x => x.Id);
            // 「取最近一份」「每天还原过几次」都按 (用户, 时间) 走。
            entity.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        });
        modelBuilder.Entity<CoreItemRecord>(entity =>
        {
            // 同 CoreCharacterRecord：自增本地键做关联，Guid 是业务身份。
            entity.HasKey(x => x.LocalId);
            entity.HasIndex(x => x.Guid).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.Quality });
            entity.HasIndex(x => new { x.EscrowUserId, x.Quality });
            entity.HasIndex(x => new { x.CatalogId });
            entity.Property(x => x.Version).IsConcurrencyToken();
        });
    }
}
