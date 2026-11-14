using System.Security.Cryptography;
using System.Text;
using OshimaBot.Game.Services;
using Xunit;
using Xunit.Abstractions;

namespace OshimaBot.Tests;

/// <summary>
/// 模拟的「同种子可复现整局」目前**不成立**，这两条用例把缺口钉在代码里，等 Core 修好后去掉 Skip 即为门禁。
/// <para>实测证据（2026-11-14，本机，<c>SeedOverride = 20261114</c>，<c>IsWeb=true</c>）：</para>
/// <list type="bullet">
/// <item>同一进程连跑 4 局：消息条数 44 / 59 / 54 / 49，摘要全不相同；</item>
/// <item>换三个独立进程各跑 1 局：条数 43 / 50 / 56，摘要全不相同；</item>
/// <item>关掉周期空投（<c>EnablePeriodicDrop=false</c>）仍然不同（36 / 33 / 44）——
///       所以不是「空投带了新随机源」这么简单；</item>
/// <item>首处分歧出现在第 60 回合附近，说明是**局内决策**分岔，不是收尾统计的问题。</item>
/// </list>
/// <para>最可疑的两处（都在引擎侧，不在本仓）：</para>
/// <list type="number">
/// <item><c>AIController</c> 用 <c>Task.Run</c> 并行评估每个移动格，候选装进 <c>ConcurrentBag</c>。
///       代码已经补了 tie-breaker（分数 → 行动类型 → 移动格 Id → 技能 Id → 物品 Id → 目标数），
///       但**没有地图时大量格子 Id 相同**，同分候选仍会落到 bag 的枚举顺序上；</item>
/// <item><c>Region.Weather</c> 用 <c>Random.Shared</c> 在内容初始化时掷天气，跨进程不可控
///       （天气影响伤害，即使决策完全确定，换台机器也会变）。</item>
/// </list>
/// <para>因此本轮只冻结**规则层与业务层**的种子样本（<see cref="SeededRegressionTests"/>），
/// 战斗整局暂时进不去冻结快照。</para>
/// </summary>
public sealed class SimulationSeedReproducibilityTests(ITestOutputHelper output)
{
    private const string SkipReason =
        "已知缺口：同种子跑不出同一局（AIController 并行评估的同分 tie-break + Region 天气用了 Random.Shared）。" +
        "详见本类注释与 migration/REMAINING.md；Core 修好后删掉 Skip 即为门禁。";

    static SimulationSeedReproducibilityTests() => FunGameRuntime.EnsureInitialized();

    [Theory(Skip = SkipReason)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameSeed_ProducesSameRun(bool isTeam)
    {
        List<string> digests = [];
        for (int run = 0; run < 3; run++)
        {
            SimulationRunContext context = new() { SeedOverride = 20261114 };
            SimulationOptions options = new() { IsWeb = true, IsTeam = isTeam, PrintOut = false };
            List<string> messages = await FunGameSimulation.StartSimulationGame(options, context);
            string digest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", messages))))[..16];
            digests.Add($"{messages.Count}:{digest}");
            output.WriteLine($"isTeam={isTeam} run={run} {digests[^1]}");
        }

        Assert.True(digests.Distinct().Count() == 1,
            $"同种子三局结果不一致（isTeam={isTeam}）：{string.Join(" / ", digests)}");
    }
}
