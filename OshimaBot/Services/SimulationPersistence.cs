using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using FunGame.Core.Entity;
using CoreCharacter = FunGame.Core.Entity.Character;
using Microsoft.EntityFrameworkCore;
using OshimaBot.Data;
using OshimaBot.Data.Entities;
using OshimaBot.Game.Services;

namespace OshimaBot.Services;

/// <summary>一局模拟的入库快照。</summary>
public sealed record SimulationRunRecord(
    long? UserId,
    int Seed,
    string OptionsJson,
    IReadOnlyList<string> Messages,
    IReadOnlyList<string> FullMessages,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt);

public interface ISimulationRunRecorder
{
    Task<long> RecordAsync(SimulationRunRecord record, CancellationToken cancellationToken = default);
}

/// <summary>
/// S9：用 SQLite 持久化模拟统计与最近日志，替代进程内字典 + rounds_archive.zip。
/// 模拟器仍直接读写内存字典（FunGame.Core 的统计对象是纯 POCO，可无损 JSON 往返），
/// 在每次运行结束时 PersistAsync 落库；应用启动时 LoadAsync 恢复，重启后状态不丢。
/// 同时实现 ISimulationRunRecorder，把每局结果写入 simulation_runs。
/// </summary>
public sealed class EfSimulationStatisticsStore(
    IServiceScopeFactory scopeFactory,
    ILogger<EfSimulationStatisticsStore> logger) : ISimulationStatisticsStore, ISimulationRunRecorder
{
    // 数据库里保留可读中文，便于直接查表复盘。
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly Lock _sync = new();

    public Dictionary<CoreCharacter, CharacterStatistics> CharacterStatistics { get; } = [];
    public Dictionary<CoreCharacter, CharacterStatistics> TeamCharacterStatistics { get; } = [];
    public IReadOnlyList<string> LastRecord { get; set; } = [];
    public IReadOnlyList<string> LastFullRecord { get; set; } = [];

    /// <summary>
    /// 由 FunGameRuntime 初始化时调用：只补齐缺失角色，不清空已加载的数据。
    /// 真正的覆盖式恢复在应用启动迁移完成后由 LoadAsync 执行。
    /// </summary>
    public void Reset(IEnumerable<CoreCharacter> characters)
    {
        lock (_sync)
        {
            foreach (CoreCharacter character in characters)
            {
                CharacterStatistics.TryAdd(character, new CharacterStatistics());
                TeamCharacterStatistics.TryAdd(character, new CharacterStatistics());
            }
        }
    }

    public T Read<T>(Func<ISimulationStatisticsStore, T> reader)
    {
        lock (_sync) return reader(this);
    }

    /// <summary>启动迁移完成后调用：从数据库恢复统计字典与最近一次模拟日志。</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        List<SimulationStatistic> rows = await db.SimulationStatistics.AsNoTracking()
            .ToListAsync(cancellationToken);
        Dictionary<(string Scope, string CatalogId), CharacterStatistics> loaded = [];
        foreach (SimulationStatistic row in rows)
        {
            CharacterStatistics? stats = DeserializeStats(row.StatsJson);
            if (stats is not null) loaded[(row.Scope, row.CatalogId)] = stats;
        }

        SimulationRun? last = await db.SimulationRuns.AsNoTracking()
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);

        lock (_sync)
        {
            CharacterStatistics.Clear();
            TeamCharacterStatistics.Clear();
            foreach (CoreCharacter character in FunGameService.Characters)
            {
                string id = character.Id.ToString(CultureInfo.InvariantCulture);
                CharacterStatistics[character] = loaded.GetValueOrDefault(("character", id))
                    ?? new CharacterStatistics();
                TeamCharacterStatistics[character] = loaded.GetValueOrDefault(("team", id))
                    ?? new CharacterStatistics();
            }
            if (last is not null)
            {
                LastRecord = DeserializeMessages(last.ResultJson);
                LastFullRecord = DeserializeMessages(last.RoundsJson);
            }
        }
        logger.LogInformation("模拟统计已从数据库恢复：{Rows} 条统计行，最近日志 {Lines} 行",
            rows.Count, LastRecord.Count);
    }

    /// <summary>一局结束后把两个统计字典 upsert 到 simulation_statistics。</summary>
    public async Task PersistAsync(CancellationToken cancellationToken = default)
    {
        List<(string Scope, string CatalogId, string Json)> snapshot = [];
        lock (_sync)
        {
            snapshot.AddRange(CharacterStatistics.Select(pair => (
                "character", pair.Key.Id.ToString(CultureInfo.InvariantCulture),
                JsonSerializer.Serialize(pair.Value, JsonOptions))));
            snapshot.AddRange(TeamCharacterStatistics.Select(pair => (
                "team", pair.Key.Id.ToString(CultureInfo.InvariantCulture),
                JsonSerializer.Serialize(pair.Value, JsonOptions))));
        }
        if (snapshot.Count == 0) return;

        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        foreach ((string scopeKey, string catalogId, string json) in snapshot)
        {
            SimulationStatistic? row = await db.SimulationStatistics.FirstOrDefaultAsync(
                x => x.Scope == scopeKey && x.CatalogId == catalogId, cancellationToken);
            if (row is null)
            {
                db.SimulationStatistics.Add(new SimulationStatistic
                {
                    Scope = scopeKey, CatalogId = catalogId, StatsJson = json,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }
            else
            {
                row.StatsJson = json;
                row.UpdatedAtUtc = DateTime.UtcNow;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>把一局结果写入 simulation_runs，并同步内存中的「上次的日志」。</summary>
    public async Task<long> RecordAsync(SimulationRunRecord record, CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        SimulationRun run = new()
        {
            UserId = record.UserId,
            Seed = record.Seed.ToString(CultureInfo.InvariantCulture),
            OptionsJson = record.OptionsJson,
            ResultJson = JsonSerializer.Serialize(record.Messages, JsonOptions),
            RoundsJson = JsonSerializer.Serialize(record.FullMessages, JsonOptions),
            StartedAtUtc = record.StartedAt.UtcDateTime,
            FinishedAtUtc = record.FinishedAt.UtcDateTime
        };
        db.SimulationRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        lock (_sync)
        {
            LastRecord = [.. record.Messages];
            LastFullRecord = [.. record.FullMessages];
        }
        return run.Id;
    }

    private static CharacterStatistics? DeserializeStats(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<CharacterStatistics>(json); }
        catch (JsonException) { return null; }
    }

    private static IReadOnlyList<string> DeserializeMessages(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }
}
