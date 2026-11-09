using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Game.Services;
using OshimaBot.Models;
using OshimaBot.Security;
using OshimaBot.Services;

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
    .ValidateOnStart();
builder.Services.AddOptions<ServerOptions>()
    .Bind(builder.Configuration.GetSection(ServerOptions.SectionName))
    .Validate(options => options.MaximumRespawnTimes is >= 0 and <= 100, "最大复活次数必须在 0 到 100 之间。")
    .ValidateOnStart();
builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "Database:ConnectionString 不能为空。")
    .ValidateOnStart();

builder.Services.AddSingleton<WebhookSignatureVerifier>();

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
builder.Services.AddSingleton<FunGameSimulationService>();
builder.Services.AddSingleton<FunGameQueryService>();
builder.Services.AddScoped<RainBotService>();
builder.Services.AddSingleton<BotMessageQueue>();
builder.Services.AddHostedService<BotMessageBackgroundService>();

WebApplication app = builder.Build();

app.UseExceptionHandler();

// 仅当 QQBot:EnableWebhook=true 时对 POST /QQBot 校验 Ed25519 签名。
app.UseMiddleware<WebhookSignatureMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

QQBotOptions botOptions = app.Services.GetRequiredService<IOptions<QQBotOptions>>().Value;
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

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    mode = botOptions.Mode.ToString(),
    environment = botOptions.Environment.ToString(),
    webhookEnabled = botOptions.EnableWebhook,
    startedAt = FunGameRuntime.StartedAt
}));

app.MapControllers();

app.Run();

public partial class Program;
