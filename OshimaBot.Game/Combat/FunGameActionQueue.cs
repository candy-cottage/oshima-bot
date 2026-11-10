using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.Queue;
using Milimoe.FunGameTesting.Others;

namespace OshimaBot.Game.Combat;

/// <summary>一场战斗的参数。默认值与旧版 <c>FunGameActionQueue</c> 对齐，可按玩法覆盖。</summary>
public sealed record BattleOptions
{
    /// <summary>回合上限；到 <c>MaxRound - 1</c> 时触发终局审判。</summary>
    public int MaxRound { get; init; } = 200;

    public int MaxRespawnTimes { get; init; }
    public int MaxScoreToWin { get; init; }

    /// <summary>固定种子；同种子同输入应得到同结果。</summary>
    public int? Seed { get; init; }

    public bool PrintOut { get; init; }
    public bool DeathMatchRoundDetail { get; init; }

    /// <summary>是否启用回合奖励特效池（旧版默认启用）。</summary>
    public bool UseRoundRewards { get; init; } = true;

    public bool BindRoundRewardsToCharacter { get; init; } = true;

    /// <summary>日志最多保留行数，超出丢弃最早的（避免长战斗占内存）。</summary>
    public int MaxLogLines { get; init; } = 4000;
}

/// <summary>战斗结果。<see cref="Queue"/> 保留完整状态，供统计、评分与结算读取。</summary>
public sealed class BattleResult
{
    public required GamingQueue Queue { get; init; }
    public Character? Winner { get; init; }
    public bool IsDraw { get; init; }
    public int Rounds { get; init; }
    public double TotalTime { get; init; }
    public IReadOnlyList<string> Log { get; init; } = [];

    /// <summary>按队伍名汇总比分（混战为空）。</summary>
    public IReadOnlyDictionary<string, int> TeamScores { get; init; } = new Dictionary<string, int>();

    public bool HasWinner => Winner is not null;

    /// <summary>取最后 N 行日志，用于 QQ 回复截断。</summary>
    public string Tail(int lines)
    {
        if (lines <= 0 || Log.Count == 0) return "";
        int skip = Math.Max(0, Log.Count - lines);
        return string.Join("\r\n", Log.Skip(skip));
    }
}

/// <summary>
/// 战斗执行器：把 Core 的 <c>GamingQueue</c> 生命周期封装成「进角色 → 打一场 → 出结果」。
/// <para>对应旧版 <c>OshimaServers/Service/FunGameActionQueue.cs</c>：初始化队列、回合奖励、
/// 逐回合 <c>NextCharacter</c>/<c>ProcessTurn</c>、终局审判、结果日志。</para>
/// <para>数值与战斗规则全部由 Core 负责，本类不写任何公式。</para>
/// </summary>
public sealed class FunGameActionQueue
{
    private readonly List<string> _log = [];
    private BattleOptions _options = new();

    /// <summary>战斗结束后的队列实例，可读取 <c>Rounds</c>、<c>TotalTime</c>、角色状态。</summary>
    public GamingQueue Queue { get; private set; } = new();

    public IReadOnlyList<string> Result => _log;

    /// <summary>多人混战（1v1 也是它）。</summary>
    public static BattleResult RunMix(IEnumerable<Character> characters, BattleOptions? options = null)
        => new FunGameActionQueue().Start([.. characters], null, options ?? new BattleOptions());

    /// <summary>分队对战。</summary>
    public static BattleResult RunTeam(IEnumerable<Team> teams, BattleOptions? options = null)
    {
        List<Team> list = [.. teams];
        return new FunGameActionQueue().Start([.. list.SelectMany(t => t.Members)], list, options ?? new BattleOptions());
    }

    public static Task<BattleResult> RunMixAsync(
        IEnumerable<Character> characters, BattleOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => RunMix(characters, options), cancellationToken);

    public static Task<BattleResult> RunTeamAsync(
        IEnumerable<Team> teams, BattleOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => RunTeam(teams, options), cancellationToken);

    private BattleResult Start(List<Character> characters, List<Team>? teams, BattleOptions options)
    {
        _options = options;
        _log.Clear();
        if (characters.Count == 0)
        {
            return new BattleResult { Queue = Queue, IsDraw = true, Log = [] };
        }

        GamingQueue queue;
        if (teams is null)
        {
            queue = new MixGamingQueue(characters, WriteLine, seed: options.Seed);
        }
        else
        {
            TeamGamingQueue teamQueue = new(characters, WriteLine, seed: options.Seed);
            foreach (Team team in teams) teamQueue.AddTeam(team.Name, team.Members);
            queue = teamQueue;
        }

        queue.MaxRespawnTimes = options.MaxRespawnTimes;
        queue.MaxScoreToWin = options.MaxScoreToWin;
        queue.InitActionQueue();
        // 与 FunGameSimulation.cs:264 一致：交给 AI 接管，否则队列会等待外部输入。
        queue.SetCharactersToAIControl(false, characters);

        // 开局已阵亡的角色直接记入淘汰，避免占行动位。
        foreach (Character dead in characters.Where(c => c.HP <= 0).ToArray())
        {
            queue.Queue.Remove(dead);
            if (!queue.Eliminated.Contains(dead)) queue.Eliminated.Add(dead);
        }

        if (options.UseRoundRewards)
        {
            // RoundRewardPool 使用内容层自己的 EffectID，用 var 避免与 Core 的同名枚举冲突。
            var pool = RoundRewardPool.Create(queue.Random);
            Dictionary<long, bool> effects = RoundRewardPool.BuildEffectMap(pool.Keys);
            queue.InitRoundRewards(effects, options.BindRoundRewardsToCharacter, id => pool[pool.Keys.First(k => (long)k == id)]);
        }

        Queue = queue;
        Character? winner = null;
        bool isDraw = false;
        int round = 1;

        while (round < options.MaxRound)
        {
            if (round == options.MaxRound - 1)
            {
                if (teams is not null)
                {
                    WriteLine("两队打到天昏地暗，流局了！！");
                    isDraw = true;
                    break;
                }

                WriteLine("=== 终局审判 ===");
                Character? chosen = characters
                    .Where(c => c.HP > 0)
                    .OrderByDescending(c => c.MaxHP <= 0 ? 0 : c.HP / c.MaxHP)
                    .ThenBy(c => c.Guid)
                    .FirstOrDefault();
                if (chosen is not null)
                {
                    WriteLine($"[ {chosen} ] 成为了天选之人！！");
                    foreach (Character other in characters.Where(c => c != chosen && c.HP > 0).ToArray())
                    {
                        WriteLine($"[ {chosen} ] 对 [ {other} ] 造成了 99999999999 点真实伤害。");
                        queue.DeathCalculation(chosen, other);
                    }
                    winner = chosen;
                }
                break;
            }

            Character? actor = queue.NextCharacter();
            if (actor is null) break;

            WriteLine($"=== Round {round++} [ Time: {queue.TotalTime} ] ===");
            WriteLine($"现在是 [ {actor} ] 的回合！");

            if (queue.ProcessTurn(actor))
            {
                break;
            }

            if (queue.LastRound.HasKill && !options.DeathMatchRoundDetail)
            {
                WriteLine(queue.LastRound.ToString().Trim());
            }
        }

        winner ??= ResolveWinner(characters, teams, out isDraw);

        Dictionary<string, int> scores = teams is null
            ? []
            : ((TeamGamingQueue)queue).Teams.Values
                .ToDictionary(t => t.Name, t => t.Score, StringComparer.Ordinal);

        return new BattleResult
        {
            Queue = queue,
            Winner = winner,
            IsDraw = isDraw,
            Rounds = queue.TotalRound,
            TotalTime = queue.TotalTime,
            Log = [.. _log],
            TeamScores = scores
        };
    }

    private static Character? ResolveWinner(
        List<Character> characters, List<Team>? teams, out bool isDraw)
    {
        isDraw = false;
        if (teams is not null)
        {
            List<Team> alive = [.. teams.Where(t => t.GetActiveCharacters().Count > 0)];
            if (alive.Count == 1)
            {
                alive[0].IsWinner = true;
                return alive[0].GetActiveCharacters()[0];
            }
            if (alive.Count > 1)
            {
                Team top = alive.OrderByDescending(t => t.Score).ThenBy(t => t.Name, StringComparer.Ordinal).First();
                top.IsWinner = true;
                return top.GetActiveCharacters().FirstOrDefault();
            }
            isDraw = true;
            return null;
        }

        List<Character> survivors = [.. characters.Where(c => c.HP > 0)];
        if (survivors.Count == 1) return survivors[0];
        if (survivors.Count > 1) return survivors.OrderByDescending(c => c.HP).ThenBy(c => c.Guid).First();
        isDraw = true;
        return null;
    }

    private void WriteLine(string line)
    {
        if (_options.PrintOut) Console.WriteLine(line);
        _log.Add(line);
        int overflow = _log.Count - _options.MaxLogLines;
        if (overflow > 0) _log.RemoveRange(0, overflow);
    }
}
