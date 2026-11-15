using System.Globalization;
using System.Text.RegularExpressions;
using OshimaBot.Models;

namespace OshimaBot.Commands;

/// <summary>
/// 模拟战报的三种粒度。引擎产出的 <c>result</c> 里混着三类块：
/// <c>=== 空投 ===</c>（每次 60 多行装备/技能清单）、<c>=== Round N ===</c>（只在该回合有人阵亡时产生）、
/// <c>==== 角色 [ X ] ====</c>（终局的角色快照，每段 4k 字）。一局合计十余万字，
/// 原样发给玩家等于让人翻 20 页却看不到重点。
/// <para>所以分三档，各自解决一个场景：</para>
/// <list type="number">
/// <item><see cref="Brief"/>：跑完一局就看这个 —— 开局 + 最后几个击杀回合 + 关键数字，通常 1–2 页；</item>
/// <item><see cref="Rounds"/>：逐个击杀回合（旧版「只给有击杀的回合」的语义），要复盘时看；</item>
/// <item><see cref="Everything"/>：原样全量，含空投与角色快照，只在核对数值时用。</item>
/// </list>
/// <para>三档都是对**同一份消息**的确定性投影，所以同一局的翻页结果永远一致 ——
/// 前提是翻页别重新跑模拟（见 CommandCatalog 里 RunSimulationAsync 的分支）。</para>
/// </summary>
public static class SimulationReplies
{
    /// <summary>一局战报的关键数字，全部从消息文本里提取，不改引擎。</summary>
    public sealed record Facts(
        int Rounds, int KillRounds, int Kills, double Seconds, string Winner, int Drops, int Sheets)
    {
        public override string ToString()
        {
            List<string> parts = [];
            // 只说「最后阵亡在第几回合」：一局可能按回合上限结束，那时最后一条击杀记录
            // 远早于真正的终局，写「打了 N 回合」就是假话。
            if (Rounds > 0) parts.Add($"最后阵亡 第 {Rounds} 回合");
            parts.Add($"{Kills} 次击杀");
            if (Seconds > 0) parts.Add($"用时 {Seconds:0.#}");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// 摘要：开局（截断）+ 最后 3 个击杀回合 + 技术得分榜 TOP5，通常 1–2 页。
    /// <para>开局那块有 60 多行「谁装备了什么」，全给就把摘要撑成一页半，所以只留前几行；
    /// 排行榜是玩家真正想看的结果，反而保留到第 5 名。</para>
    /// </summary>
    public static BotReply Brief(IReadOnlyList<string> messages, int page, string command, int? seed)
    {
        Facts facts = Read(messages);
        List<string> blocks = [];

        string? opening = messages.FirstOrDefault();
        if (opening is not null) blocks.Add(TrimLines(opening, 8, "开局"));

        blocks.AddRange(messages.Where(IsRound).TakeLast(3));

        // 结尾那一块（排行榜 / 名次）：既不是回合也不是空投、角色快照，通常就是它。
        string? board = messages.LastOrDefault(block => !IsDrop(block) && !IsRound(block) && !IsSheet(block));
        if (board is not null && board != opening)
        {
            blocks.Add(TrimRanking(board, 3));
        }

        blocks.Add($"（共 {facts.Drops} 次空投、{facts.Sheets} 段角色数据未列出）");
        return CommandReplies.LogPages(Title("🎲 模拟结果", seed), blocks, page, command, facts.ToString(),
            ["/上次的日志", "/上次的完整日志"]);
    }

    /// <summary>逐个击杀回合（旧版模拟的粒度）+ 结尾的排行榜，不含空投与角色快照。</summary>
    public static BotReply Rounds(IReadOnlyList<string> messages, int page, string command, int? seed)
    {
        Facts facts = Read(messages);
        List<string> blocks = [.. messages.Where(IsRound)];
        string? board = messages.LastOrDefault(block => !IsDrop(block) && !IsRound(block) && !IsSheet(block));
        if (board is not null) blocks.Add(board);
        if (blocks.Count == 0) return BotReply.FromText("上一次模拟没有任何回合阵亡记录。");
        return CommandReplies.LogPages(Title("📜 上次的模拟日志", seed), blocks, page, command,
            facts.ToString(), ["/上次的完整日志", "/FunGame模拟"]);
    }

    /// <summary>原样全量：连空投清单与角色快照一起给。</summary>
    public static BotReply Everything(IReadOnlyList<string> messages, int page, string command, int? seed)
        => CommandReplies.LogPages(Title("📜 上次的完整日志", seed), messages, page, command,
            Read(messages).ToString(), ["/上次的日志"]);

    public static Facts Read(IReadOnlyList<string> messages)
    {
        int rounds = 0;
        int killRounds = 0;
        int kills = 0;
        int drops = 0;
        int sheets = 0;
        double seconds = 0;
        string winner = "";

        foreach (string block in messages)
        {
            if (IsDrop(block)) { drops++; continue; }
            if (IsSheet(block)) { sheets++; continue; }
            if (!IsRound(block)) continue;
            killRounds++;

            Match round = Regex.Match(block, @"=== Round (\d+) \[ Time: ([\d.]+) \] ===");
            if (round.Success)
            {
                rounds = Math.Max(rounds, int.Parse(round.Groups[1].Value, CultureInfo.InvariantCulture));
                seconds = Math.Max(seconds, double.Parse(round.Groups[2].Value, CultureInfo.InvariantCulture));
            }

            kills += Regex.Matches(block, "杀死了").Count;
            foreach (string line in block.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("冠军：", StringComparison.Ordinal))
                {
                    // 「冠军：大島シヤ [ 第一滴血 ] …」→ 只取名字
                    string name = trimmed["冠军：".Length..];
                    int bracket = name.IndexOf(" [", StringComparison.Ordinal);
                    winner = (bracket > 0 ? name[..bracket] : name).Trim();
                }
                else if (winner.Length == 0 && trimmed.EndsWith("是胜利者。", StringComparison.Ordinal))
                {
                    winner = trimmed.Trim('[', ']').Replace(" ] 是胜利者。", "", StringComparison.Ordinal).Trim();
                }
            }
        }

        return new Facts(rounds, killRounds, kills, seconds, winner, drops, sheets);
    }

    /// <summary>只留前几行，其余收进 /上次的完整日志（摘要里塞满装备清单没人看）。</summary>
    private static string TrimLines(string block, int keep, string label)
    {
        string[] lines = block.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length <= keep) return block;
        return string.Join("\n", lines.Take(keep))
            + $"\n……（{label}共 {lines.Length} 行，完整内容见 /上次的完整日志）";
    }

    /// <summary>排行榜只留前 N 名；每名占一行标题 + 几行统计，按「6. [」这类行号切开。</summary>
    private static string TrimRanking(string block, int top)
    {
        string[] lines = block.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string marker = $"{top + 1}. [";
        int cut = Array.FindIndex(lines, line => line.StartsWith(marker, StringComparison.Ordinal));
        if (cut < 0) return block;
        return string.Join("\n", lines.Take(cut))
            + $"\n……（排行榜其余名次见 /上次的日志）";
    }

    private static string Title(string prefix, int? seed)
        => seed is > 0 ? $"{prefix} · 种子 {seed}" : prefix;

    private static bool IsDrop(string block) => block.StartsWith("=== 空投", StringComparison.Ordinal);

    private static bool IsRound(string block) => block.StartsWith("=== Round", StringComparison.Ordinal);

    private static bool IsSheet(string block) => block.StartsWith("=== 角色 [", StringComparison.Ordinal);
}
