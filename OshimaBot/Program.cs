using Microsoft.AspNetCore.Authentication;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OshimaBot.Commands;
using OshimaBot.Data;
using OshimaBot.Data.Core;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;
using OshimaBot.Messaging;
using OshimaBot.Models;
using OshimaBot.Security;
using OshimaBot.Services;
using OshimaBot.Services.Gateway;

FunGameRuntime.EnsureInitialized();

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddMemoryCache();

builder.Services.AddOptions<QQBotOptions>()
    .Bind(builder.Configuration.GetSection(QQBotOptions.SectionName))
    .Validate(options => !options.Enabled || options.IsConfigured, "启用 QQBot 时必须配置 AppId 和 Secret。")
    .Validate(options => options.RequestTimeoutSeconds is > 0 and <= 300, "QQBot 请求超时必须在 1 到 300 秒之间。")
    .Validate(options => options.Intents > 0, "QQBot Intents 必须大于 0。")
    .Validate(options => options.SignatureMaxAgeSeconds is > 0 and <= 3600, "回调签名时间戳窗口必须在 1 到 3600 秒之间。")
    .Validate(options => !options.EnableWebhook || !string.IsNullOrWhiteSpace(options.Secret), "启用 Webhook 回调时必须配置 Secret 用于验签。")
    .Validate(options => options.ShardId >= 0, "QQBot ShardId 不能为负数。")
    .Validate(options => options.GatewayMaxReconnectDelaySeconds is > 0 and <= 600, "网关重连退避上限必须在 1 到 600 秒之间。")
    .ValidateOnStart();
builder.Services.AddOptions<ServerOptions>()
    .Bind(builder.Configuration.GetSection(ServerOptions.SectionName))
    .Validate(options => options.MaximumRespawnTimes is >= 0 and <= 100, "最大复活次数必须在 0 到 100 之间。")
    .Validate(options => options.BindCodeTtlSeconds is >= 30 and <= 3600, "绑定码有效期必须在 30 到 3600 秒之间。")
    .ValidateOnStart();
builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "Database:ConnectionString 不能为空。")
    .ValidateOnStart();

builder.Services.AddSingleton<WebhookSignatureVerifier>();
builder.Services.AddAuthentication(ApiKeyDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyDefaults.Scheme, null);
builder.Services.AddAuthorization(options => options.AddPolicy(ApiKeyDefaults.Policy, policy => policy
    .AddAuthenticationSchemes(ApiKeyDefaults.Scheme)
    .RequireAuthenticatedUser()));

builder.Services.AddDbContext<BotDbContext>(options =>
{
    string connectionString = builder.Configuration.GetValue<string>(
        $"{DatabaseOptions.SectionName}:ConnectionString") ?? "Data Source=oshimobot.db";
    SqliteConnectionStringBuilder sqlite = new(connectionString);
    if (!Path.IsPathRooted(sqlite.DataSource))
    {
        // 相对路径统一基于 ContentRootPath，保证应用运行时与 EF CLI 指向同一个库文件。
        sqlite.DataSource = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, sqlite.DataSource));
    }
    options.UseSqlite(sqlite.ToString());
});
builder.Services.AddHostedService<DatabaseInitializer>();

builder.Services.AddHttpClient(QQBotService.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("QQBot:RequestTimeoutSeconds", 30));
});
builder.Services.AddSingleton<IQQBotService, QQBotService>();
// S9：统计与模拟运行记录统一由 EF Core 实现；模拟器、查询服务与后台任务共享同一实例。
builder.Services.AddSingleton<EfSimulationStatisticsStore>();
builder.Services.AddSingleton<ISimulationStatisticsStore>(sp => sp.GetRequiredService<EfSimulationStatisticsStore>());
builder.Services.AddSingleton<ISimulationRunRecorder>(sp => sp.GetRequiredService<EfSimulationStatisticsStore>());
builder.Services.AddSingleton<FunGameSimulationService>();
builder.Services.AddSingleton<FunGameQueryService>();
builder.Services.AddScoped<MessageSender>();
builder.Services.AddSingleton<IUserSessionService, UserSessionService>();
// 业务时间与随机源：业务判定统一走 IGameClock / IRandomSource，便于用假时钟与定种子验证。
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IGameClock, GameClock>();
builder.Services.AddSingleton<IRandomSource, SystemRandomSource>();
builder.Services.AddSingleton<IPlayerService, PlayerService>();
builder.Services.AddSingleton<IEconomyService, EconomyService>();
// 多人房间：进程内注册表（与旧版 FunGameConstant.Rooms 一致，重启即清空）。
builder.Services.AddSingleton<IRoomService, RoomService>();
// v3.1：Core 语义存储（Credits/Materials 为 double、QualityType 0-6、6 装备槽、实体快照）。
builder.Services.AddSingleton<CoreEntityStore>();
builder.Services.AddSingleton(sp => new CommandDependencies(
    sp.GetRequiredService<FunGameQueryService>(),
    sp.GetRequiredService<FunGameSimulationService>(),
    sp.GetRequiredService<IBindCodeService>(),
    sp.GetRequiredService<IUserSessionService>(),
    sp.GetRequiredService<IPlayerService>(),
    sp.GetRequiredService<IEconomyService>(),
    sp.GetRequiredService<IRoomService>(),
    sp.GetRequiredService<ILoggerFactory>().CreateLogger("OshimaBot.Commands")));
builder.Services.AddSingleton(sp => CommandCatalog.Build(sp.GetRequiredService<CommandDependencies>()));
builder.Services.AddSingleton<CommandRouter>();
builder.Services.AddScoped<RainBotService>();
builder.Services.AddSingleton<BotMessageQueue>();
builder.Services.AddSingleton<IBotMessageSink>(sp => sp.GetRequiredService<BotMessageQueue>());
builder.Services.AddSingleton<IEventDeduplicationStore, EventDeduplicationStore>();
builder.Services.AddSingleton<IInteractionAckService, InteractionAckService>();
builder.Services.AddSingleton<IQQEventDispatcher, QQEventDispatcher>();
builder.Services.AddSingleton<ApiKeyAuthenticator>();
builder.Services.AddSingleton<IUserDirectory, UserDirectory>();
builder.Services.AddSingleton<IBindCodeService, BindCodeService>();
builder.Services.AddSingleton<IGatewayApiClient, GatewayApiClient>();
builder.Services.AddSingleton<IGatewaySessionStore, GatewaySessionStore>();
builder.Services.AddSingleton<IGatewayStatus, GatewayStatus>();
builder.Services.AddHostedService<ProcessedEventCleanupService>();
builder.Services.AddHostedService<BotMessageBackgroundService>();
builder.Services.AddHostedService<QQBotGatewayClient>();

WebApplication app = builder.Build();

// S9：在首次模拟 / 统计读写前把 EF Core 仓储注入模拟器。
// InitializeStatistics 只为新仓储补齐字典；真正的数据库恢复由 DatabaseInitializer.LoadAsync 在迁移后完成。
EfSimulationStatisticsStore statisticsStore = app.Services.GetRequiredService<EfSimulationStatisticsStore>();
FunGameSimulation.UseStatisticsStore(statisticsStore);
FunGameSimulation.InitializeStatistics();

app.UseExceptionHandler();

// 仅当 QQBot:EnableWebhook=true 时对 POST /QQBot 校验 Ed25519 签名。
app.UseMiddleware<WebhookSignatureMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

QQBotOptions botOptions = app.Services.GetRequiredService<IOptions<QQBotOptions>>().Value;
IGatewayStatus gatewayStatus = app.Services.GetRequiredService<IGatewayStatus>();
app.Logger.LogInformation(
    "OshimaBot 启动：Mode={Mode}, Environment={Environment}, Webhook={WebhookEnabled}, Intents={Intents}",
    botOptions.Mode, botOptions.Environment, botOptions.EnableWebhook, botOptions.Intents);

app.MapGet("/", () => Results.Ok(new
{
    name = "OshimaBot",
    description = "Oshima FunGame WebAPI",
    core = "FunGame.Core v3",
    startedAt = FunGameRuntime.StartedAt,
    characters = FunGameService.Characters.Count,
    skills = FunGameService.AllSkills.Count,
    items = FunGameService.AllItems.Count
}));

app.MapGet("/health", () =>
{
    GatewayStatusSnapshot gateway = gatewayStatus.Snapshot;
    return Results.Ok(new
    {
        status = "ok",
        mode = botOptions.Mode.ToString(),
        environment = botOptions.Environment.ToString(),
        webhookEnabled = botOptions.EnableWebhook,
        gateway = new
        {
            connected = gateway.Connected,
            sessionId = gateway.SessionId,
            lastSeq = gateway.LastSeq,
            connectedAtUtc = gateway.ConnectedAtUtc,
            lastError = gateway.LastError
        },
        startedAt = FunGameRuntime.StartedAt
    });
});

app.MapControllers();

app.Run();

public partial class Program;
