using OshimaBot.Game.Services;
using OshimaBot.Models;
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
    .ValidateOnStart();
builder.Services.AddOptions<ServerOptions>()
    .Bind(builder.Configuration.GetSection(ServerOptions.SectionName))
    .Validate(options => options.MaximumRespawnTimes is >= 0 and <= 100, "最大复活次数必须在 0 到 100 之间。")
    .ValidateOnStart();

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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

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
app.MapControllers();

app.Run();

public partial class Program;
