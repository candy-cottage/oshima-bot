using System.Text.Encodings.Web;
using System.Text.Json;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// 内容目录指纹（计划 §10 第 7 条）：内容变了必须有人知道。
/// <para>内容包编译在 <c>OshimaBot.Content</c> 里，换一版不会改任何配置，而玩家背包与角色快照存的是
/// <c>CatalogId</c>——删改内容后这些引用会静默失效。这里把「当前内容长什么样」冻成一个指纹：
/// 改了物品/技能/角色的 Id、名字、品质，指纹就变，测试会指出来，逼你确认「这是发版，还是手滑」。</para>
/// <para>重新冻结（确认是**有意**的内容变更之后）：</para>
/// <code>OSHIMA_UPDATE_CONTENT=1 dotnet test --filter ContentCatalog</code>
/// <para>指纹只吃 Id / 名字 / 品质，不吃描述与文案：改说明文字不该算换内容。</para>
/// </summary>
public sealed class ContentCatalogTests
{
    private const string UpdateVariable = "OSHIMA_UPDATE_CONTENT";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly string ProjectDirectory =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));

    private static string FixturePath =>
        Path.GetFullPath(Path.Combine(ProjectDirectory, "..", "migration", "content-catalog.json"));

    static ContentCatalogTests() => FunGameRuntime.EnsureInitialized();

    private static bool Updating
        => Environment.GetEnvironmentVariable(UpdateVariable) is "1" or "true" or "TRUE";

    [Fact]
    public void Fingerprint_IsStableWithinTheProcess()
    {
        Assert.Equal(ContentCatalog.Fingerprint, ContentCatalog.Fingerprint);
        Assert.Equal(64, ContentCatalog.Fingerprint.Length);
        Assert.Equal(ContentCatalog.Fingerprint[..12], ContentCatalog.ShortFingerprint);
    }

    [Fact]
    public void Fingerprint_MatchesTheRecordedCatalog()
    {
        ContentCatalogSummary summary = ContentCatalog.Summary;
        ContentCatalogRecord record = Updating ? new ContentCatalogRecord() : ReadRecord();

        if (Updating)
        {
            record.SchemaVersion = 1;
            record.Version = GameplayRules.CatalogVersion;
            record.Fingerprint = ContentCatalog.Fingerprint;
            record.Items = summary.Items;
            record.Skills = summary.Skills;
            record.Characters = summary.Characters;
            record.Regions = summary.Regions;
            WriteRecord(record);
            return;
        }

        Assert.True(record.Fingerprint.Length == 64,
            $"migration/content-catalog.json 里的 Fingerprint 不是 64 位：{record.Fingerprint}；"
            + $"重新冻结：{UpdateVariable}=1 dotnet test --filter ContentCatalog");

        Assert.True(string.Equals(record.Fingerprint, ContentCatalog.Fingerprint, StringComparison.Ordinal),
            $"""
             内容目录变了。
               记录：{record.Fingerprint[..12]}（物品 {record.Items} / 技能 {record.Skills} / 角色 {record.Characters} / 地区 {record.Regions}，版本 {record.Version}）
               现在：{ContentCatalog.ShortFingerprint}（{summary}，版本 {GameplayRules.CatalogVersion}）
             若这是有意的内容改动：{UpdateVariable}=1 dotnet test --filter ContentCatalog，并确认 CatalogVersion 是否需要提升；
             否则说明有人改了物品/技能/角色的 Id、名字或品质 —— 存量背包里的 CatalogId 可能已经对不上了。
             """);
    }

    [Fact]
    public void Version_MatchesTheRecordedCatalog()
    {
        if (Updating) return;   // 上面那条用例负责整体重写

        ContentCatalogRecord record = ReadRecord();
        Assert.True(record.Version == GameplayRules.CatalogVersion,
            $"内容目录版本不一致：记录 {record.Version}，代码 {GameplayRules.CatalogVersion}。" +
            "两头必须一起改，否则「哪一版内容」这个问题永远答不上来。");
    }

    // ---------------- 夹具读写 ----------------

    private static ContentCatalogRecord ReadRecord()
    {
        Assert.True(File.Exists(FixturePath),
            $"找不到 migration/content-catalog.json。生成它：{UpdateVariable}=1 dotnet test --filter ContentCatalog");
        ContentCatalogRecord? record = JsonSerializer.Deserialize<ContentCatalogRecord>(File.ReadAllText(FixturePath));
        Assert.NotNull(record);
        return record;
    }

    private static void WriteRecord(ContentCatalogRecord record)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FixturePath)!);
        File.WriteAllText(FixturePath, JsonSerializer.Serialize(record, Json) + "\n", new System.Text.UTF8Encoding(false));
    }

    /// <summary>与 <c>migration/content-catalog.json</c> 一一对应。</summary>
    private sealed class ContentCatalogRecord
    {
        public int SchemaVersion { get; set; } = 1;

        /// <summary>与 <see cref="GameplayRules.CatalogVersion"/> 一致。</summary>
        public string Version { get; set; } = "";

        public string Fingerprint { get; set; } = "";

        public int Items { get; set; }

        public int Skills { get; set; }

        public int Characters { get; set; }

        public int Regions { get; set; }
    }
}
