using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OshimaBot.Commands;
using OshimaBot.Game.Services;
using OshimaBot.Models;
using OshimaBot.Services;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// 战报的三档粒度与「翻页不重跑」。
/// <para>背景（2026-11-16 玩家反馈）：<c>/FunGame模拟</c> 一局十余万字（空投清单 + 每秒伤害 + 角色快照），
/// 界面切成 20 页；更要命的是 <c>/FunGame模拟 p2</c> 会**再跑一局**（新种子），
/// 于是「第 2 页」是另一局的第 2 页 —— 种子、阵容、胜负全变，翻页毫无意义。</para>
/// <para>这里用与引擎同形的合成消息锁住两件事：三档投影各给什么、带 pN 时到底跑没跑。</para>
/// </summary>
public sealed class SimulationReplyTests
{
    private const int Seed = 20261114;

    /// <summary>与引擎同形的消息：开局块（含种子行）、击杀回合、周期空投、终局角色快照。</summary>
    private static List<string> BuildReport(int rounds = 6, int filler = 300)
    {
        List<string> messages =
        [
            "=== 空投 ===\r\n[种子] 本局模拟使用随机种子 20261114（同种子可复现开局与配装）。\r\n"
            + "社区送温暖了，现在随机发放空投！！\r\n" + new string('装', 300)
        ];

        for (int i = 1; i <= rounds; i++)
        {
            string tail = i == rounds
                ? "=== 排名 ===\r\n冠军：甲 [ 第一滴血 ]\r\n亚军：乙\r\n"
                : "";
            messages.Add(
                $"=== Round {i * 10} [ Time: {(i * 33.5).ToString("0.##", CultureInfo.InvariantCulture)} ] ===\r\n"
                + $"[ 甲 ] 对 [ 乙 ] 造成了 100 点伤害\r\n[ 甲 ] 杀死了 [ 乙 ]，获得 33 金币！\r\n"
                + tail + new string('伤', filler));
        }

        messages.Add("=== 空投 ===\r\n社区送温暖了，现在随机发放空投！！\r\n" + new string('空', filler));
        messages.Add("=== 角色 [ 乙 ] ===\r\n乙 - 等级 60\r\n" + new string('角', filler));
        return messages;
    }

    private static string Text(BotReply reply) => reply.Markdown?.Content ?? reply.Text ?? "";

    /// <summary>把一条消息的全部页拼起来：断言「某块在不在」时不该被页码干扰。</summary>
    private static string AllPages(Func<int, BotReply> render)
    {
        List<string> pages = [];
        for (int page = 1; page <= 50; page++)
        {
            string text = Text(render(page));
            pages.Add(text);
            Match marker = Regex.Match(text, @"第 (\d+)/(\d+) 页");
            if (!marker.Success) break;
            if (int.Parse(marker.Groups[1].Value, CultureInfo.InvariantCulture)
                >= int.Parse(marker.Groups[2].Value, CultureInfo.InvariantCulture)) break;
        }
        return string.Join("\n", pages);
    }

    [Fact]
    public void Facts_AreReadFromTheReportItself()
    {
        SimulationReplies.Facts facts = SimulationReplies.Read(BuildReport());

        Assert.Equal(60, facts.Rounds);        // 最后一条回合头 = 60
        Assert.Equal(6, facts.KillRounds);     // 6 个击杀回合
        Assert.Equal(6, facts.Kills);          // 每个回合一次「杀死了」
        Assert.Equal("甲", facts.Winner);      // 从「冠军：」取
        Assert.Equal(2, facts.Drops);
        Assert.Equal(1, facts.Sheets);
    }

    [Fact]
    public void Brief_KeepsTheOpeningAndTheLastRounds()
    {
        List<string> report = BuildReport();
        string text = AllPages(page => SimulationReplies.Brief(report, page, "/FunGame模拟", Seed));

        Assert.Contains("种子 20261114", text);
        Assert.Contains("[种子] 本局模拟使用随机种子", text);   // 开局（第一条）
        Assert.Contains("=== Round 60", text);                 // 结局那一回合
        Assert.Contains("冠军：甲", text);
        Assert.DoesNotContain("=== Round 10", text);           // 只有最后 3 个回合
        Assert.DoesNotContain("=== 角色 [", text);              // 角色快照不进摘要
        Assert.Contains("次空投", text);                        // 空投只留计数
    }

    [Fact]
    public void Rounds_DropDropsAndSheets()
    {
        List<string> report = BuildReport();
        string text = AllPages(page => SimulationReplies.Rounds(report, page, "/上次的日志", Seed));

        Assert.Contains("=== Round 10", text);      // 全部击杀回合都在
        Assert.Contains("=== Round 60", text);
        Assert.DoesNotContain("社区送温暖", text);   // 空投清单不要
        Assert.DoesNotContain("=== 角色 [", text);   // 角色快照不要
    }

    [Fact]
    public void Everything_KeepsEveryBlock()
    {
        List<string> report = BuildReport();
        string text = AllPages(page => SimulationReplies.Everything(report, page, "/上次的完整日志", Seed));

        Assert.Contains("社区送温暖", text);
        Assert.Contains("=== 角色 [ 乙 ]", text);
        Assert.Contains("=== Round 10", text);
        Assert.Contains("第 1/", text);             // 全量确实分了多页
        Assert.Contains("第 2/", text);
    }

    /// <summary>
    /// 同一局的第 2 页必须稳定：既不能重新跑模拟（换种子），也不能每次投影出别的页。
    /// 用「记录器有没有被调用」当证据 —— 真跑过一局就会写 simulation_runs。
    /// </summary>
    [Fact]
    public async Task Paging_ReadsTheCachedRunAndNeverRunsAgain()
    {
        List<string> report = BuildReport(rounds: 12, filler: 900);
        InMemorySimulationStatisticsStore store = new()
        {
            LastRecord = [.. report],
            LastFullRecord = [.. report],
            LastSeed = Seed
        };
        CountingRecorder recorder = new();
        FunGameSimulationService simulations = new(
            store, recorder, Options.Create(new ServerOptions()), NullLogger<FunGameSimulationService>.Instance);

        CommandDependencies dependencies = new(
            null!, simulations, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, NullLogger.Instance);
        CommandRegistry registry = CommandCatalog.Build(dependencies);
        ICommand command = registry.FindByKey("sim")!;

        BotReply second = await command.ExecuteAsync(Context(registry, dependencies, "/FunGame模拟 p2", 2, true));

        Assert.Equal(0, recorder.Count);                                        // 没有新的一局
        Assert.Contains("第 2/", Text(second));                                  // 确实是第 2 页
        Assert.Equal(Text(SimulationReplies.Brief(report, 2, "/FunGame模拟", Seed)), Text(second));
    }

    [Fact]
    public async Task Paging_WithoutAnyRun_SaysSoInsteadOfStartingOne()
    {
        CountingRecorder recorder = new();
        FunGameSimulationService simulations = new(
            new InMemorySimulationStatisticsStore(), recorder,
            Options.Create(new ServerOptions()), NullLogger<FunGameSimulationService>.Instance);

        CommandDependencies dependencies = new(
            null!, simulations, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, NullLogger.Instance);
        CommandRegistry registry = CommandCatalog.Build(dependencies);

        BotReply reply = await registry.FindByKey("sim")!.ExecuteAsync(
            Context(registry, dependencies, "/FunGame模拟 p2", 2, true));

        Assert.Equal(0, recorder.Count);
        Assert.Contains("还没有模拟结果", Text(reply));
    }

    private static CommandContext Context(
        CommandRegistry registry, CommandDependencies dependencies, string raw, int page, bool pageRequested)
        => new()
        {
            Message = new ThirdPartyMessage
            {
                Id = "MSG-SIM", Detail = raw, IsGroup = false, OpenId = "USER",
                AuthorOpenId = "USER", FunGameUID = 0, Timestamp = "2026-11-16 00:00:00"
            },
            Raw = raw,
            Verb = "FunGame模拟",
            Args = [],
            Page = page,
            PageRequested = pageRequested,
            Registry = registry,
            Dependencies = dependencies,
            Data = new OtherData(),
            CancellationToken = CancellationToken.None
        };

    private sealed class CountingRecorder : ISimulationRunRecorder
    {
        public int Count { get; private set; }

        public Task<long> RecordAsync(SimulationRunRecord record, CancellationToken cancellationToken = default)
        {
            Count++;
            return Task.FromResult((long)Count);
        }
    }
}
