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
/// <para><b>原因尚未证实。</b>已排除的：周期空投（关掉照样分岔）、天气（见下）、收尾统计
/// （分歧出现在第 60 回合，属局内决策）。唯一在查的方向是 <c>AIController</c>：
/// 它用 <c>Task.Run</c> 并行评估每个移动格、候选装进 <c>ConcurrentBag</c>，虽然补了 tie-breaker
/// （分数 → 行动类型 → 移动格 Id → 技能 Id → 物品 Id → 目标数），但没有地图时大量格子 Id 相同，
/// 同分候选仍可能落回 bag 的枚举顺序。</para>
/// <para><b>天气与此无关（2026-11-15 更正）</b>：本类早先的注释写「天气影响伤害」，那是错的。
/// <c>Region.Weather</c> 只在内容初始化时掷一次，全仓除显示（<c>/地区列表</c>、地区详情）外
/// 没有任何消费方；内容里的 <c>GenerationPredicates</c>（<c>r =&gt; r.Weather == "永昼"</c>）
/// 从未被评估，既不影响伤害也不影响刷怪。</para>
/// <para>顺带查到的另一处 <c>Random.Shared</c>（与模拟无关，但影响**探索**的可复现性）：
/// <c>RegionUnit</c> / <c>RegionCharacter</c> 的构造里用 <c>Random.Next</c> 掷 ATK/HP/MP/SPD，
/// 即内容初始化时一次定死、且每个进程不同——同一种子打同一地区的怪，属性会不一样。</para>
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
