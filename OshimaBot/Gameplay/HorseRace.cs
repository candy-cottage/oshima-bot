using System.Text;

namespace OshimaBot.Gameplay;

/// <summary>赛马技能：加成可以作用于自己，也可以作用于对手（<see cref="ToEnemy"/>）。</summary>
public sealed class HorseSkill
{
    public RaceHorse? Horse { get; set; }
    public string Name { get; set; } = "";
    public bool ToEnemy { get; set; }
    public int AddStep { get; set; }
    public int ReduceStep { get; set; }
    public int AddHP { get; set; }
    public int ReduceHP { get; set; }
    public int AddHR { get; set; }
    public int ReduceHR { get; set; }
    public int ChangePosition { get; set; }

    /// <summary>发动概率，1 表示每回合都发动。</summary>
    public double CastProbability { get; set; } = 1;

    /// <summary>持续回合数；1 表示立即生效并结束。</summary>
    public int Duration { get; set; } = 1;

    public HorseSkill Clone(RaceHorse? horse) => new()
    {
        Horse = horse,
        Name = Name,
        ToEnemy = ToEnemy,
        AddStep = AddStep,
        ReduceStep = ReduceStep,
        AddHP = AddHP,
        ReduceHP = ReduceHP,
        AddHR = AddHR,
        ReduceHR = ReduceHR,
        ChangePosition = ChangePosition,
        CastProbability = CastProbability,
        Duration = Duration
    };

    public override string ToString()
    {
        StringBuilder builder = new();
        if (AddStep > 0) builder.Append($"每回合将额外移动 {AddStep} 步！");
        if (ReduceStep > 0) builder.Append($"每回合将少移动 {ReduceStep} 步！");
        if (AddHP > 0) builder.Append($"恢复了 {AddHP} 点生命值！");
        if (ReduceHP > 0) builder.Append($"受到了 {ReduceHP} 点伤害！");
        if (AddHR > 0) builder.Append($"每回合将额外恢复 {AddHR} 点生命值！");
        if (ReduceHR > 0) builder.Append($"每回合将少恢复 {ReduceHR} 点生命值！");
        if (ChangePosition != 0) builder.Append($"{(ChangePosition > 0 ? "前进" : "后退")}了 {Math.Abs(ChangePosition)} 步！");
        return builder.ToString().Trim();
    }
}

/// <summary>正在生效的持续技能，记录剩余回合数。</summary>
public sealed class ActiveHorseEffect(HorseSkill skill)
{
    public HorseSkill Skill { get; } = skill;
    public int RemainDuration { get; set; } = skill.Duration;
}

/// <summary>参赛马匹（一名玩家一匹，名字取玩家显示名）。</summary>
public sealed class RaceHorse(long userId, string name)
{
    private int _step = 1;
    private int _hpRecovery = 1;
    private int _hp = 3;

    public long UserId { get; } = userId;
    public string Name { get; } = name;

    /// <summary>每回合行动的步数。</summary>
    public int Step
    {
        get => _step;
        set => _step = Math.Max(0, value);
    }

    public int MaxHP { get; set; } = 3;

    /// <summary>当前生命值（0 表示退出比赛）。</summary>
    public int HP
    {
        get => _hp;
        set => _hp = Math.Min(MaxHP, Math.Max(0, value));
    }

    /// <summary>每回合恢复的生命值。</summary>
    public int HPRecovery
    {
        get => _hpRecovery;
        set => _hpRecovery = Math.Max(0, value);
    }

    /// <summary>当前在赛道上的位置。</summary>
    public int Position { get; set; }

    /// <summary>永久技能。</summary>
    public HashSet<HorseSkill> Skills { get; } = [];

    /// <summary>正在生效的持续效果。</summary>
    public List<ActiveHorseEffect> ActiveEffects { get; } = [];

    public override string ToString() => Name;
}

/// <summary>一匹马的最终成绩。</summary>
/// <param name="Rank">名次；并列同名次。</param>
/// <param name="Tied">是否与他人并列。</param>
public sealed record HorseRaceEntry(
    long UserId, string Name, int Rank, bool Tied, int Points, int Position, int HP);

/// <summary>一场比赛的结果：名次与逐回合战报。</summary>
public sealed record HorseRaceOutcome(
    int TrackLength, bool Finished, IReadOnlyList<HorseRaceEntry> Ranking, IReadOnlyList<string> Log);

/// <summary>
/// 赛马小游戏（旧版 <c>HorseRacing.RunHorseRacing</c> 的移植）。
/// <para>与战斗完全无关：马匹只有步数 / 生命 / 回血三个数值，靠随机技能与随机事件赛跑，
/// 谁先跑到赛道终点谁赢。全程只用 <see cref="IRandomSource"/>，同种子必然复现同一场比赛。</para>
/// <para>积分口径同旧版：第 1 名 10 分，之后每降一名 ×0.8（向下取整、最低 1 分）。</para>
/// </summary>
public static class HorseRace
{
    private const int MaxTurns = 100;
    private const double RandomEventChance = 0.3;

    public static HorseRaceOutcome Run(IReadOnlyList<(long UserId, string Name)> entrants, IRandomSource random)
    {
        if (entrants.Count == 0) return new HorseRaceOutcome(0, false, [], []);

        List<RaceHorse> horses = [];
        foreach ((long userId, string name) in entrants)
        {
            RaceHorse horse = new(userId, name);
            AssignRandomSkills(horse, random);
            horses.Add(horse);
        }

        List<string> log = [];
        StringBuilder head = new();
        head.AppendLine("☆--- 参赛选手 ---☆");
        foreach (RaceHorse horse in horses)
        {
            head.AppendLine($"[ {horse} ] 已准备就绪！初始步数: {horse.Step}, 生命值: {horse.HP}, 每回合恢复生命值: {horse.HPRecovery}");
            if (horse.Skills.Count != 0)
            {
                head.AppendLine($"[ {horse} ] 拥有技能: {string.Join("，",
                    horse.Skills.Select(skill => $"{skill.Name}（持续 {skill.Duration} 回合）"))}");
            }
        }
        head.AppendLine();
        head.AppendLine("☆--- 比赛开始！ ---☆");

        int trackLength = random.Next(8, 16);
        head.AppendLine($"本次抽取赛道长度：{trackLength} 步！");
        log.Add(head.ToString().Trim());

        bool finished = false;
        for (int turn = 1; turn <= MaxTurns && !finished; turn++)
        {
            StringBuilder builder = new();
            builder.AppendLine($"☆--- 第 {turn} 回合 ---☆");

            // 先统一掷技能，本回合结算，否则技能要下回合才生效。
            Dictionary<RaceHorse, List<(HorseSkill Skill, RaceHorse Target)>> cast = [];
            foreach (RaceHorse horse in horses)
            {
                cast[horse] = [];
                if (horse.HP == 0) continue;
                foreach (HorseSkill skill in horse.Skills)
                {
                    if (random.NextDouble() >= skill.CastProbability) continue;
                    RaceHorse target = horse;
                    if (skill.ToEnemy)
                    {
                        List<RaceHorse> others = [.. horses.Where(x => x != horse)];
                        if (others.Count == 0) continue;
                        target = others[random.Next(others.Count)];
                    }
                    cast[horse].Add((skill, target));
                    target.ActiveEffects.Add(new ActiveHorseEffect(skill));
                }
            }

            List<RaceHorse> winners = [];
            Dictionary<RaceHorse, int> turnSteps = [];
            foreach (RaceHorse horse in horses)
            {
                turnSteps[horse] = 0;
                int effectiveStep = horse.Step;
                int effectiveRecovery = horse.HPRecovery;
                List<string> events = [];

                foreach ((HorseSkill skill, RaceHorse target) in cast[horse])
                {
                    events.Add(skill.Duration > 0
                        ? $"✨ 发动了 [ {skill.Name} ]，持续 {skill.Duration} 回合！{(skill.ToEnemy ? $"[ {target} ] " : "")}{skill}"
                        : $"✨ 发动了 [ {skill.Name} ]！{(skill.ToEnemy ? $"[ {target} ] " : "")}{skill}");
                }

                // 结算身上的持续效果（可能是别人施加的）。
                List<ActiveHorseEffect> expired = [];
                foreach (ActiveHorseEffect effect in horse.ActiveEffects)
                {
                    HorseSkill skill = effect.Skill;
                    effectiveStep += skill.AddStep - skill.ReduceStep;
                    effectiveRecovery += skill.AddHR - skill.ReduceHR;
                    horse.HP += skill.AddHP - skill.ReduceHP;
                    horse.Position += skill.ChangePosition;
                    turnSteps[horse] += skill.ChangePosition;

                    RaceHorse? source = skill.Horse;
                    events.Add(source is not null && source != horse
                        ? $"💥 受到了 [ {skill.Name}（来自：{source}）] 的影响，{skill}（剩余 {effect.RemainDuration} 回合）"
                        : $"💥 受到了 [ {skill.Name} ] 的影响，{skill}（剩余 {effect.RemainDuration} 回合）");

                    effect.RemainDuration--;
                    if (effect.RemainDuration <= 0) expired.Add(effect);
                }
                foreach (ActiveHorseEffect effect in expired) horse.ActiveEffects.Remove(effect);

                // 随机事件：赛道上踩到加速带、泥泞区之类。
                if (random.NextDouble() < RandomEventChance)
                {
                    HorseSkill incident = RandomEventSkill(random);
                    if (incident.Duration > 0)
                    {
                        horse.ActiveEffects.Add(new ActiveHorseEffect(incident));
                        events.Add($"💥 遭遇随机事件 [ {incident.Name} ]，持续 {incident.Duration} 回合！{incident}");
                    }
                    else
                    {
                        effectiveStep += incident.AddStep - incident.ReduceStep;
                        effectiveRecovery += incident.AddHR - incident.ReduceHR;
                        horse.HP += incident.AddHP - incident.ReduceHP;
                        events.Add($"💥 遭遇随机事件 [ {incident.Name} ]！{incident}");
                    }
                }

                int beforeHeal = horse.HP;
                horse.HP += effectiveRecovery;
                if (beforeHeal != horse.HP) events.Add($"❤️ 生命值恢复至 {horse.HP} 点（+{effectiveRecovery}）。");

                if (horse.HP <= 0)
                {
                    turnSteps[horse] = 0;
                    if (events.Count != 0) builder.AppendLine($"[ {horse} ] {string.Join("；", events)}");
                    continue;
                }

                effectiveStep = Math.Max(0, effectiveStep);
                horse.Position += effectiveStep;
                turnSteps[horse] += effectiveStep;
                if (events.Count != 0) builder.AppendLine($"[ {horse} ] {string.Join("；", events)}");

                if (horse.Position >= trackLength)
                {
                    winners.Add(horse);
                    builder.AppendLine($"🎯 [ {horse} ] 冲过终点线！");
                    finished = true;
                }
            }

            builder.AppendLine("☆--- 赛道状况 ---☆");
            for (int i = 0; i < horses.Count; i++)
            {
                builder.AppendLine(RenderTrack(horses[i], i + 1, trackLength, turnSteps));
            }
            if (finished)
            {
                builder.AppendLine($"🎯 赢家：[ {string.Join(" ] / [ ", winners.Select(x => x.Name))} ]！");
            }
            log.Add(builder.ToString().Trim());
        }

        // 名次：位置降序，位置相同则并列；并列会占用后续名次。
        List<RaceHorse> ranking = [.. horses.OrderByDescending(h => h.Position)];
        List<HorseRaceEntry> entries = [];
        int rank = 1;
        int tiedAtCurrentRank = 0;
        int lastPosition = -1;
        for (int i = 0; i < ranking.Count; i++)
        {
            RaceHorse horse = ranking[i];
            if (i == 0)
            {
                rank = 1;
                lastPosition = horse.Position;
                tiedAtCurrentRank = 1;
            }
            else if (horse.Position == lastPosition || horse.Position >= trackLength)
            {
                tiedAtCurrentRank++;
            }
            else
            {
                rank += tiedAtCurrentRank;
                lastPosition = horse.Position;
                tiedAtCurrentRank = 1;
            }
            entries.Add(new HorseRaceEntry(
                horse.UserId, horse.Name, rank, tiedAtCurrentRank > 1,
                CalculatePoints(rank), horse.Position, horse.HP));
        }

        StringBuilder result = new();
        result.AppendLine("☆--- 比赛结果 ---☆");
        foreach (HorseRaceEntry entry in entries)
        {
            result.AppendLine($"{(entry.Tied ? "并列" : "")}第 {entry.Rank} 名：{entry.Name}"
                + $"（{entry.Position} 步，获得 {entry.Points} 点赛马积分）");
        }
        log.Add(result.ToString().Trim());

        return new HorseRaceOutcome(trackLength, finished, entries, log);
    }

    /// <summary>名次 → 积分：第 1 名 10 分，之后每名 ×0.8，最低 1 分（旧版 <c>CalculatePointsForRank</c>）。</summary>
    public static int CalculatePoints(int rank)
    {
        if (rank <= 0) return 0;
        int points = 10;
        for (int r = 2; r <= rank; r++)
        {
            points = (int)(points * 0.8);
            if (points == 0) points = 1;
        }
        return points;
    }

    private static void AssignRandomSkills(RaceHorse horse, IRandomSource random)
    {
        List<HorseSkill> pool =
        [
            new() { Name = "冲刺", AddStep = 2, CastProbability = 0.3, Duration = 1 },
            new() { Name = "耐力爆发", AddHP = 2, CastProbability = 0.3, Duration = 1 },
            new() { Name = "神行百变", AddStep = 1, AddHR = 1, CastProbability = 0.3, Duration = 3 },
            new() { Name = "稳健", AddHR = 1, CastProbability = 0.3, Duration = 2 },
            new() { Name = "疾风步", AddStep = 1, CastProbability = 0.15, Duration = 2 },
            new() { Name = "疲惫", ToEnemy = true, ReduceStep = 1, CastProbability = 0.4, Duration = 2 },
            new() { Name = "摔跤", ToEnemy = true, ReduceHP = 2, ReduceStep = 1, CastProbability = 0.4, Duration = 1 },
            new() { Name = "干扰", ToEnemy = true, ReduceStep = 2, CastProbability = 0.3, Duration = 1 },
            new() { Name = "石头攻击", ToEnemy = true, ReduceHP = 3, CastProbability = 0.4, Duration = 1 },
            new() { Name = "台风", ToEnemy = true, ReduceHP = 2, CastProbability = 0.4, Duration = 2 },
            new() { Name = "死刑宣告", ToEnemy = true, ReduceHP = 2, ReduceHR = 1, CastProbability = 0.3, Duration = 2 },
            new() { Name = "后退吧！", ToEnemy = true, ChangePosition = -2, CastProbability = 0.3, Duration = 1 }
        ];

        int count = random.Next(1, 4);
        for (int i = 0; i < count; i++)
        {
            HorseSkill candidate = pool[random.Next(pool.Count)];
            while (horse.Skills.Any(skill => skill.Name == candidate.Name))
            {
                candidate = pool[random.Next(pool.Count)];
            }
            horse.Skills.Add(candidate.Clone(horse));
        }
    }

    private static HorseSkill RandomEventSkill(IRandomSource random)
    {
        List<HorseSkill> pool =
        [
            new() { Name = "加速带", AddStep = 3, Duration = 1 },
            new() { Name = "泥泞区", ReduceStep = 2, Duration = 2 },
            new() { Name = "观众欢呼", AddHP = 2, Duration = 1 },
            new() { Name = "小石子", ReduceHP = 1, Duration = 1 },
            new() { Name = "顺风", AddStep = 1, Duration = 3 },
            new() { Name = "逆风", ReduceStep = 1, Duration = 2 },
            new() { Name = "兴奋剂", AddStep = 2, Duration = 4, ReduceHP = 2 }
        ];
        return pool[random.Next(pool.Count)];
    }

    /// <summary>赛道可视行：`[1]|====&lt;马(HP)&gt;==|(位置)(本回合步数)`。</summary>
    private static string RenderTrack(RaceHorse horse, int trackNumber, int trackLength, Dictionary<RaceHorse, int> turnSteps)
    {
        if (horse.Position < 0) horse.Position = 0;
        int before = Math.Min(horse.Position, trackLength);
        int after = Math.Max(0, trackLength - horse.Position);

        string marker = $"<{horse.Name}({horse.HP})>";
        if (horse.HP == 0) marker = $"💀<{horse.Name}>";
        if (horse.ActiveEffects.Count > 0)
        {
            marker += $"[{string.Join("][", horse.ActiveEffects.Select(effect => effect.Skill.Name))}]";
        }
        int step = turnSteps.GetValueOrDefault(horse);
        return $"[{trackNumber}]|{new string('=', after)}{marker}{new string('=', before)}"
            + $"|({horse.Position})({(step >= 0 ? "+" : "")}{step})";
    }
}
