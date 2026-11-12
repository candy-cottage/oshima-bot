using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OshimaBot.Data;
using OshimaBot.Game.Combat;
using OshimaBot.Game.Services;
using FunGame.Core.Library.Constant;
using CoreCharacter = FunGame.Core.Entity.Character;
using CoreTeam = FunGame.Core.Entity.Team;

namespace OshimaBot.Gameplay;

/// <summary>房间成员视图。</summary>
public sealed record RoomMemberInfo(long UserId, string Display);

public sealed record RoomSummary(
    string Code, string KindName, string MasterName, int Members, int MaxUsers, bool HasPassword, bool Playing);

public sealed record RoomDetail(
    string Code, string KindName, string MasterName, long MasterId, int MaxUsers,
    bool HasPassword, bool Playing, bool IsMaster, IReadOnlyList<RoomMemberInfo> Members);

/// <summary>
/// 多人房间（旧版 <c>OnlineService</c>）：创建 / 加入 / 退出 / 列表 / 信息 / 开始。
/// <para>状态**只在进程内存里**——与旧版 <c>FunGameConstant.Rooms</c> 一致。
/// 房间是短生命周期会话态，玩家存档与战绩仍然落库，所以重启丢房间不丢数据。</para>
/// </summary>
public interface IRoomService
{
    /// <param name="scope">群 OpenId：房间按群隔离（旧版 <c>GameMap = groupId</c>）。</param>
    Task<ActionResult> CreateAsync(long userId, string scope, string kind, string? password,
        CancellationToken cancellationToken = default);

    Task<ActionResult> JoinAsync(long userId, string scope, string code, string? password,
        CancellationToken cancellationToken = default);

    /// <summary>旧版「加入赛马」：直接加入本群那个赛马房间，不用记房号。</summary>
    Task<ActionResult> JoinHorseRoomAsync(long userId, string scope, CancellationToken cancellationToken = default);

    Task<ActionResult> QuitAsync(long userId, string scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// 开一局。只有房主能开（赛马房间例外），开局期间房间被锁在 <c>Gaming</c>。
    /// <para>返回摘要 + 逐回合日志：房间对局与赛马战报都可能上百行，交给指令层分页。</para>
    /// </summary>
    Task<BattleReport> StartAsync(long userId, string scope, CancellationToken cancellationToken = default);

    IReadOnlyList<RoomSummary> List(string scope);

    /// <summary><paramref name="code"/> 为空时查看自己所在的房间。</summary>
    RoomDetail? Info(string scope, string? code, long userId);
}

internal sealed class GameRoom
{
    public required string Code { get; init; }
    public required RoomKind Kind { get; init; }
    public required string Scope { get; init; }
    public required string Password { get; init; }
    public required long MasterId { get; set; }
    public required DateTime CreatedAtUtc { get; set; }
    public RoomState State { get; set; } = RoomState.Created;
    public List<RoomMemberInfo> Members { get; } = [];

    public bool HasPassword => Password.Length > 0;
    public int MaxUsers => RoomRules.MaxUsers(Kind);
}

/// <summary>
/// 房间注册表：一把锁 + 两张字典（房号 → 房间、玩家 → 房号，保证一个人只在一个房间）。
/// <para>开局是**两段式**：先在锁内把状态改成 <see cref="RoomState.Gaming"/>（挡住并发重复开赛），
/// 再在锁外跑战斗，最后回锁恢复状态。战斗可能跑上百回合，不能一直占着锁。</para>
/// </summary>
public sealed class RoomService(
    IServiceScopeFactory scopeFactory,
    IGameClock clock,
    IRandomSource random,
    ILogger<RoomService> logger) : IRoomService
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, GameRoom> _rooms = [];
    private readonly Dictionary<long, string> _userRoom = [];
    private readonly HashSet<string> _horseScopes = [];

    public async Task<ActionResult> CreateAsync(long userId, string scope, string kind, string? password,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0) return PlayerService.NoIdentity;
        if (string.IsNullOrWhiteSpace(scope))
        {
            return ActionResult.Fail("请先在群里发起", "没有创建任何房间",
                "多人房间只能在群聊里使用，私聊暂时不支持", "/帮助");
        }
        RoomKind? parsed = RoomRules.ParseKind(kind);
        if (parsed is null)
        {
            return ActionResult.Fail("房间类型无效", "没有创建任何房间",
                $"可用类型：{RoomRules.KindHint()}", "/帮助");
        }
        RoomKind roomKind = parsed.Value;
        string pass = NormalizePassword(password);
        if (pass.Length > RoomRules.MaxPasswordLength)
        {
            return ActionResult.Fail("房间密码太长", "没有创建任何房间",
                $"密码最长 {RoomRules.MaxPasswordLength} 个字符", "/帮助");
        }

        string display = await DisplayOfAsync(userId, cancellationToken);

        GameRoom room;
        lock (_sync)
        {
            Sweep(clock.UtcNow);
            if (_userRoom.TryGetValue(userId, out string? existing) && _rooms.TryGetValue(existing, out GameRoom? other))
            {
                return ActionResult.Fail("你已经在一个房间里了", "没有创建任何房间",
                    $"先 /退出房间（当前在 [ {other.Code} ] {RoomRules.Name(other.Kind)}）", "/退出房间");
            }
            if (roomKind == RoomKind.HorseRacing && _horseScopes.Contains(scope))
            {
                return ActionResult.Fail("本群已经有赛马房间", "没有创建任何房间",
                    $"空闲房间会在 {RoomRules.ExpireMinutes} 分钟后自动解散，等它结束或换群再建", "/房间列表");
            }

            room = new GameRoom
            {
                Code = NewCode(),
                Kind = roomKind,
                Scope = scope,
                Password = pass,
                MasterId = userId,
                CreatedAtUtc = clock.UtcNow
            };
            room.Members.Add(new RoomMemberInfo(userId, display));
            _rooms[room.Code] = room;
            _userRoom[userId] = room.Code;
            if (roomKind == RoomKind.HorseRacing) _horseScopes.Add(scope);
        }

        List<string> lines =
        [
            $"- 房号：[ {room.Code} ]　类型：{RoomRules.Name(roomKind)}　上限 {room.MaxUsers} 人",
            room.HasPassword ? $"- 已设密码：{new string('*', room.Password.Length)}" : "- 未设密码，本群任何人都能加入",
            $"- 空闲 {RoomRules.ExpireMinutes} 分钟会自动解散",
            $"- 其他人用 /加入房间 {room.Code}" + (room.HasPassword ? " 密码" : ""),
            roomKind == RoomKind.HorseRacing
                ? "- 赛马房间：任何成员都可以 /开始游戏"
                : "- 你是房主：人齐后由你 /开始游戏"
        ];
        return ActionResult.Success("🏠 房间创建成功", $"[ {room.Code} ] {RoomRules.Name(roomKind)}",
            lines, ["/房间信息", "/加入房间 " + room.Code, "/房间列表"]);
    }

    public async Task<ActionResult> JoinAsync(long userId, string scope, string code, string? password,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0) return PlayerService.NoIdentity;
        if (string.IsNullOrWhiteSpace(scope))
        {
            return ActionResult.Fail("请先在群里发起", "没有加入任何房间",
                "多人房间只能在群聊里使用", "/帮助");
        }
        string normalized = NormalizeCode(code);
        if (normalized.Length == 0)
        {
            return ActionResult.Fail("没有给房号", "没有加入任何房间",
                "用法：/加入房间 房号 [密码]", "/房间列表");
        }

        string display = await DisplayOfAsync(userId, cancellationToken);

        GameRoom room;
        lock (_sync)
        {
            Sweep(clock.UtcNow);
            if (!_rooms.TryGetValue(normalized, out GameRoom? found))
            {
                return ActionResult.Fail($"找不到房间 [ {normalized} ]", "没有加入任何房间",
                    "房间可能已解散，用 /房间列表 看看当前有哪些", "/房间列表");
            }
            room = found;
            if (!string.Equals(room.Scope, scope, StringComparison.Ordinal))
            {
                return ActionResult.Fail("这个房号不属于本群", "没有加入任何房间",
                    "房间按群隔离，只能加入本群的房间", "/房间列表");
            }
            if (_userRoom.TryGetValue(userId, out string? mine))
            {
                if (string.Equals(mine, room.Code, StringComparison.Ordinal))
                {
                    return ActionResult.Fail("你已经在这个房间里了", "没有重复加入", "等房主 /开始游戏", "/房间信息");
                }
                return ActionResult.Fail("你已经在另一个房间里了", "没有加入新房间",
                    $"先 /退出房间（当前在 [ {mine} ]）", "/退出房间");
            }
            if (room.State != RoomState.Created)
            {
                return ActionResult.Fail("这局已经开始了", "没有加入任何房间",
                    "等这一局结束，或者自己 /创建房间", "/房间列表");
            }
            if (room.Members.Count >= room.MaxUsers)
            {
                return ActionResult.Fail("房间已满", "没有加入任何房间",
                    $"{RoomRules.Name(room.Kind)} 上限 {room.MaxUsers} 人", "/房间列表");
            }
            if (room.HasPassword && !string.Equals(room.Password, NormalizePassword(password), StringComparison.Ordinal))
            {
                return ActionResult.Fail("房间密码不正确", "没有加入任何房间",
                    "用法：/加入房间 房号 密码", "/房间列表");
            }
            room.Members.Add(new RoomMemberInfo(userId, display));
            _userRoom[userId] = room.Code;
        }

        return ActionResult.Success("🚪 已加入房间",
            $"[ {room.Code} ] {RoomRules.Name(room.Kind)} · {room.Members.Count}/{room.MaxUsers} 人",
            [
                $"- 成员：{string.Join("、", room.Members.Select(x => x.Display))}",
                room.Members.Count < RoomRules.MinMembers
                    ? $"- 还差至少 {RoomRules.MinMembers - room.Members.Count} 人才能开局"
                    : "- 人够了，等房主 /开始游戏"
            ],
            ["/房间信息", "/退出房间", "/开始游戏"]);
    }

    public async Task<ActionResult> JoinHorseRoomAsync(long userId, string scope, CancellationToken cancellationToken = default)
    {
        if (userId <= 0) return PlayerService.NoIdentity;
        if (string.IsNullOrWhiteSpace(scope))
        {
            return ActionResult.Fail("请先在群里发起", "没有加入任何房间",
                "多人房间只能在群聊里使用", "/帮助");
        }

        string? code;
        lock (_sync)
        {
            Sweep(clock.UtcNow);
            code = _rooms.Values
                .FirstOrDefault(room => room.Kind == RoomKind.HorseRacing
                                        && string.Equals(room.Scope, scope, StringComparison.Ordinal))?.Code;
        }
        if (code is null)
        {
            return ActionResult.Fail("本群还没有赛马房间", "没有加入任何房间",
                "先让人 /创建赛马 开一间", "/创建赛马");
        }
        return await JoinAsync(userId, scope, code, null, cancellationToken);
    }

    public Task<ActionResult> QuitAsync(long userId, string scope, CancellationToken cancellationToken = default)
    {
        if (userId <= 0) return Task.FromResult(PlayerService.NoIdentity);

        string? message = null;
        string? status = null;
        List<string> lines = [];
        lock (_sync)
        {
            Sweep(clock.UtcNow);
            if (!_userRoom.TryGetValue(userId, out string? code) || !_rooms.TryGetValue(code, out GameRoom? room))
            {
                return Task.FromResult(ActionResult.Fail("你当前不在任何房间中", "没有退出任何房间",
                    "用 /房间列表 查看本群的房间", "/房间列表"));
            }
            if (!string.Equals(room.Scope, scope, StringComparison.Ordinal))
            {
                return Task.FromResult(ActionResult.Fail("房间不属于本群", "没有退出任何房间",
                    "请回到建房的群里操作", "/帮助"));
            }

            room.Members.RemoveAll(x => x.UserId == userId);
            _userRoom.Remove(userId);
            status = $"[ {room.Code} ] {RoomRules.Name(room.Kind)}";
            message = "已退出房间";

            if (room.Members.Count == 0)
            {
                RemoveRoom(room.Code);
                lines.Add("- 房间已无人，已自动解散");
            }
            else
            {
                if (room.MasterId == userId)
                {
                    room.MasterId = room.Members[0].UserId;
                    lines.Add($"- 房主转让给：{room.Members[0].Display}");
                }
                lines.Add($"- 剩余成员：{string.Join("、", room.Members.Select(x => x.Display))}");
            }
        }

        return Task.FromResult(ActionResult.Success("🚶 已离开房间", $"{message} · {status}", lines,
            ["/房间列表", "/状态"]));
    }

    public async Task<BattleReport> StartAsync(long userId, string scope, CancellationToken cancellationToken = default)
    {
        if (userId <= 0) return BattleReport.Empty(PlayerService.NoIdentity);

        GameRoom room;
        List<RoomMemberInfo> members;
        lock (_sync)
        {
            Sweep(clock.UtcNow);
            if (!_userRoom.TryGetValue(userId, out string? code) || !_rooms.TryGetValue(code, out GameRoom? found))
            {
                return BattleReport.Empty(ActionResult.Fail("你当前不在任何房间中", "没有开始游戏",
                    "先 /创建房间 或 /加入房间", "/房间列表"));
            }
            room = found;
            if (!string.Equals(room.Scope, scope, StringComparison.Ordinal))
            {
                return BattleReport.Empty(ActionResult.Fail("房间不属于本群", "没有开始游戏", "请回到建房的群里操作", "/帮助"));
            }
            if (room.State != RoomState.Created)
            {
                return BattleReport.Empty(ActionResult.Fail("这一局正在进行中", "没有重复开局",
                    "等战报出来再开下一局", "/房间信息"));
            }
            if (room.MasterId != userId && room.Kind != RoomKind.HorseRacing)
            {
                return BattleReport.Empty(ActionResult.Fail("你不是房主，无法开始游戏", "没有开始游戏",
                    "让房主说 /开始游戏，赛马房间除外", "/房间信息"));
            }
            if (room.Members.Count < RoomRules.MinMembers)
            {
                return BattleReport.Empty(ActionResult.Fail("房间人数不足", "没有开始游戏",
                    $"至少要 {RoomRules.MinMembers} 人，现在 {room.Members.Count} 人", "/加入房间 " + room.Code));
            }
            if (!RoomRules.IsPlayable(room.Kind))
            {
                return BattleReport.Empty(ActionResult.Fail($"{RoomRules.Name(room.Kind)}还没接上玩法", "没有开始游戏",
                    "房间可以先建着，混战 / 团战现在就能打；赛马与共斗在后续切片接入", "/帮助"));
            }

            room.State = RoomState.Gaming;   // 单飞锁：并发开赛只有第一个能进到战斗
            members = [.. room.Members];
        }

        try
        {
            return room.Kind switch
            {
                RoomKind.HorseRacing => await RunHorseRaceAsync(room, members, cancellationToken),
                RoomKind.Cooperative => await RunCoopRoomAsync(room, members, cancellationToken),
                RoomKind.Mix => await RunMixRoomAsync(room, members, cancellationToken),
                RoomKind.Team => await RunTeamRoomAsync(room, members, cancellationToken),
                _ => BattleReport.Empty(ActionResult.Fail("玩法尚未接入", "没有开始游戏", "后续切片会补上", "/帮助"))
            };
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "房间对局失败：Room={Code}", room.Code);
            return BattleReport.Empty(ActionResult.Fail("对局出现异常", "这一局已作废", "稍后重试，或 /退出房间", "/房间列表"));
        }
        finally
        {
            lock (_sync)
            {
                if (_rooms.ContainsKey(room.Code))
                {
                    room.State = RoomState.Created;
                    room.CreatedAtUtc = clock.UtcNow;    // 开局也算活跃，重新计空闲
                }
            }
        }
    }

    public IReadOnlyList<RoomSummary> List(string scope)
    {
        lock (_sync)
        {
            Sweep(clock.UtcNow);
            return [.. _rooms.Values
                .Where(x => string.Equals(x.Scope, scope, StringComparison.Ordinal))
                .OrderBy(x => x.CreatedAtUtc)
                .Select(x => new RoomSummary(
                    x.Code, RoomRules.Name(x.Kind), DisplayOf(x.MasterId), x.Members.Count, x.MaxUsers,
                    x.HasPassword, x.State == RoomState.Gaming))];
        }
    }

    public RoomDetail? Info(string scope, string? code, long userId)
    {
        lock (_sync)
        {
            Sweep(clock.UtcNow);
            GameRoom? room;
            if (string.IsNullOrWhiteSpace(code))
            {
                room = _userRoom.TryGetValue(userId, out string? mine) && _rooms.TryGetValue(mine, out GameRoom? own)
                    ? own
                    : null;
            }
            else
            {
                _rooms.TryGetValue(NormalizeCode(code), out room);
            }
            if (room is null || !string.Equals(room.Scope, scope, StringComparison.Ordinal)) return null;

            return new RoomDetail(
                room.Code, RoomRules.Name(room.Kind), DisplayOf(room.MasterId), room.MasterId, room.MaxUsers,
                room.HasPassword, room.State == RoomState.Gaming, room.MasterId == userId, [.. room.Members]);
        }
    }

    /// <summary>清理空闲超时的房间；每次房间操作前顺手跑一遍，等价于旧版定时任务 <c>RoomsAutoDisband</c>。</summary>
    public void Sweep()
    {
        lock (_sync) Sweep(clock.UtcNow);
    }

    // ---------------- 内部 ----------------

    /// <summary>
    /// 赛马：不打架，跑一场。积分按名次记到**个人**的赛马积分（旧版按群存在 PluginConfig，本作改个人）。
    /// <para>参赛者是房间成员本身，与角色无关——马匹的步数/生命/回血由小游戏自己生成。</para>
    /// </summary>
    private async Task<BattleReport> RunHorseRaceAsync(
        GameRoom room, IReadOnlyList<RoomMemberInfo> members, CancellationToken ct)
    {
        HorseRaceOutcome outcome = HorseRace.Run([.. members.Select(x => (x.UserId, x.Display))], random);
        long awarded = await AwardHorsePointsAsync(room.Code, outcome.Ranking, ct);

        List<string> lines =
        [
            $"- 房间：[ {room.Code} ] {RoomRules.Name(room.Kind)}　赛道 {outcome.TrackLength} 步"
            + (outcome.Finished ? "" : "（跑满回合，按最终位置排名）"),
            $"- 参赛：{members.Count} 人",
            "- 名次与积分："
        ];
        lines.AddRange(outcome.Ranking.Select(entry =>
            $"  · {(entry.Tied ? "并列" : "")}第 {entry.Rank} 名 {entry.Name}：{entry.Position} 步 · 积分 +{entry.Points}"));
        lines.Add($"- 本局共发放 {awarded} 点赛马积分（记在个人账户），可到 /商店6 兑换");

        HorseRaceEntry champion = outcome.Ranking[0];
        return new BattleReport(
            ActionResult.Success("🐎 赛马结束",
                $"{champion.Name} 冲线 · 赛马积分 +{champion.Points}",
                lines, ["/开始游戏", "/商店6", "/房间信息"]),
            outcome.Log);
    }

    /// <summary>按名次发放赛马积分；走 <c>ApplyCurrency</c> 所以每笔都有 ledger。</summary>
    private async Task<long> AwardHorsePointsAsync(
        string roomCode, IReadOnlyList<HorseRaceEntry> ranking, CancellationToken ct)
    {
        List<HorseRaceEntry> rewarded = [.. ranking.Where(entry => entry.Points > 0)];
        if (rewarded.Count == 0) return 0;

        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        long total = 0;
        foreach (HorseRaceEntry entry in rewarded)
        {
            PlayerAccount account = await PlayerService.EnsureAsync(db, entry.UserId, ct);
            PlayerService.ApplyCurrency(db, account, entry.Points, "horse_points", "horse_racing", "room", roomCode);
            account.UpdatedAtUtc = clock.UtcNow;
            total += entry.Points;
        }
        await db.SaveChangesAsync(ct);
        return total;
    }

    /// <summary>
    /// 共斗（旧版 <c>Cooperative.RunCooperativeGame</c>）：房间成员各出自家的主战角色组一队，
    /// 一起去打一波按平均等级 +5 生成的敌人。
    /// <para>旧版三个口径照搬：敌人数量 = 参战人数、胜利发 <c>普通等级</c> 的钻石与共斗积分、
    /// 失败按「击杀数 / 敌人总数」折算；MVP 额外拿一份钻石。</para>
    /// <para>旧版还有「主战 HP &lt; 10% 不许进场」的门槛——本作角色没有持久 HP（只在战斗内存在），
    /// 这条门槛丢掉，改为要求每位成员都有主战角色，避免有人白嫖战绩。</para>
    /// </summary>
    private async Task<BattleReport> RunCoopRoomAsync(
        GameRoom room, IReadOnlyList<RoomMemberInfo> members, CancellationToken ct)
    {
        List<(RoomMemberInfo Member, CoreCharacter Fighter)> fighters = await LoadFightersAsync(members, ct);
        List<string> missing = [.. members
            .Where(member => fighters.All(fighter => fighter.Member.UserId != member.UserId))
            .Select(member => member.Display)];
        if (missing.Count > 0)
        {
            return BattleReport.Empty(ActionResult.Fail("有成员还没有主战角色", "没有开始讨伐",
                $"{string.Join("、", missing)} 先用 /注册 建档或 /抽卡 拿角色，也可以让他先 /退出房间",
                "/我的角色"));
        }
        if (fighters.Count == 0)
        {
            return BattleReport.Empty(ActionResult.Fail("没有可出战的角色", "没有开始讨伐",
                "房间里没有人有主战角色", "/我的角色"));
        }

        // 动态难度：旧版 cLevel = 平均等级 + 5，技能/魔法/普攻等级按 7 / 9 折算。
        int coopLevel = Math.Max(1, (int)fighters.Average(x => x.Fighter.Level) + 5);
        int skillLevel = Math.Max(1, coopLevel / 7);
        int magicLevel = Math.Max(1, coopLevel / 9);
        int attackLevel = magicLevel;

        Random local = GameplayRules.NewRandom(random);
        List<CoreCharacter> enemies = [];
        for (int i = 0; i < fighters.Count; i++)
        {
            CoreCharacter template = FunGameService.Characters[local.Next(FunGameService.Characters.Count)];
            CoreCharacter enemy = CombatCharacterFactory.Build(template, coopLevel);
            string baseName = $"共斗敌人·{template.Name}";
            int same = enemies.Count(x => x.Name.StartsWith(baseName, StringComparison.Ordinal));
            enemy.Name = same == 0 ? baseName : $"{baseName}{Duplicates(same)}";
            enemy.NormalAttack.Level = attackLevel;
            BossEnhance.EquipRedGear(enemy, local);
            BossEnhance.EquipMagicCardPack(enemy, local, magicLevel);
            BossEnhance.AddBossSkills(enemy, local, skillLevel);
            BossEnhance.ApplyDynamics(enemy, coopLevel, "共斗敌人被动");
            enemy.Recovery();
            enemies.Add(enemy);
        }

        BattleResult battle = FunGameActionQueue.RunTeam(
            [new CoreTeam("讨伐队", [.. fighters.Select(x => x.Fighter)]), new CoreTeam("共斗敌人", enemies)],
            new BattleOptions { MaxRound = 200, UseRoundRewards = false });

        bool win = enemies.All(enemy => enemy.HP <= 0);
        int killed = enemies.Count(enemy => enemy.HP <= 0);
        int award = RoomRules.CoopReward(coopLevel, enemies.Count, killed);

        // MVP 取技术得分最高的参战角色（旧版口径）。
        CoreCharacter? mvp = battle.Queue.CharacterStatistics
            .Where(entry => fighters.Any(fighter => ReferenceEquals(fighter.Fighter, entry.Key)))
            .OrderByDescending(entry => entry.Value.Rating)
            .Select(entry => entry.Key)
            .FirstOrDefault();
        // 一个敌人都没杀掉就不发 MVP 奖——旧版在这里也会发，但「全员没奖励、MVP 却拿钻石」不合理。
        int mvpBonus = mvp is null || award <= 0 ? 0 : coopLevel / 5;

        await AwardCoopRewardsAsync(room.Code, fighters, award, mvp, mvpBonus, ct);

        List<string> lines =
        [
            $"- 房间：[ {room.Code} ] {RoomRules.Name(room.Kind)}　敌人 {enemies.Count} 名（Lv.{coopLevel}）",
            $"- 我方：{string.Join("、", fighters.Select(x => $"{x.Member.Display} Lv.{x.Fighter.Level}"))}",
            win
                ? $"- 结果：全员击杀成功（{battle.Rounds} 回合）· 每人钻石 +{award} · 共斗积分 +{award}"
                : killed > 0
                    ? $"- 结果：只击杀 {killed}/{enemies.Count} 名（{battle.Rounds} 回合）· 每人钻石 +{award} · 共斗积分 +{award}"
                    : $"- 结果：一个敌人都没杀掉（{battle.Rounds} 回合）· 没有奖励",
            mvp is null || mvpBonus <= 0
                ? "- MVP：本场无有效统计"
                : $"- MVP：{NameOf(fighters, mvp)}　额外钻石 +{mvpBonus}",
            "- 战斗数据："
        ];
        lines.AddRange(fighters
            .Select(fighter => (fighter.Member, fighter.Fighter))
            .Select(entry =>
            {
                var stats = battle.Queue.CharacterStatistics
                    .Where(pair => ReferenceEquals(pair.Key, entry.Fighter))
                    .Select(pair => pair.Value)
                    .FirstOrDefault();
                string score = stats is null
                    ? "无统计"
                    : $"评分 {stats.Rating:0.0#} · 伤害 {stats.TotalDamage:0.0} · 击杀 {stats.Kills} · 死亡 {stats.Deaths}";
                return $"  · {entry.Member.Display}（{entry.Fighter.Name}）：{score}";
            }));
        if (!win)
        {
            lines.Add("- 敌人按队伍平均等级 +5 强化（红装 + 增益特效），等级低时很难打，先 /练级 或换更强的装备再来");
        }
        lines.Add("- 共斗积分可以在 /商店7 兑换奖券与突破材料");

        return new BattleReport(
            ActionResult.Success(win ? "🐉 共斗胜利" : "🛡️ 共斗失败",
                win ? $"钻石 +{award} · 共斗积分 +{award}" : $"击杀 {killed}/{enemies.Count} · 钻石 +{award}",
                lines, ["/开始游戏", "/商店7", "/房间信息"]),
            battle.Log);
    }

    /// <summary>共斗结算：每人一份钻石 + 共斗积分，MVP 再加一份钻石；都走 ledger。</summary>
    private async Task AwardCoopRewardsAsync(
        string roomCode, List<(RoomMemberInfo Member, CoreCharacter Fighter)> fighters,
        int award, CoreCharacter? mvp, int mvpBonus, CancellationToken ct)
    {
        if (award <= 0 && mvpBonus <= 0) return;

        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        foreach ((RoomMemberInfo member, CoreCharacter fighter) in fighters)
        {
            PlayerAccount account = await PlayerService.EnsureAsync(db, member.UserId, ct);
            if (award > 0)
            {
                PlayerService.ApplyCurrency(db, account, award, "diamonds", "coop_reward", "room", roomCode);
                PlayerService.ApplyCurrency(db, account, award, "coop_points", "coop_reward", "room", roomCode);
            }
            if (mvpBonus > 0 && mvp is not null && ReferenceEquals(fighter, mvp))
            {
                PlayerService.ApplyCurrency(db, account, mvpBonus, "diamonds", "coop_mvp", "room", roomCode);
            }
            account.UpdatedAtUtc = clock.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>同名敌人的序号后缀（旧版用希腊字母，这里用更直观的中文序数）。</summary>
    private static string Duplicates(int index) => index switch
    {
        1 => "·二",
        2 => "·三",
        3 => "·四",
        _ => $"·{index + 1}"
    };

    private async Task<BattleReport> RunMixRoomAsync(
        GameRoom room, IReadOnlyList<RoomMemberInfo> members, CancellationToken ct)
    {
        List<(RoomMemberInfo Member, CoreCharacter Fighter)> fighters = await LoadFightersAsync(members, ct);
        if (fighters.Count < RoomRules.MinMembers)
        {
            return BattleReport.Empty(ActionResult.Fail("参战角色不足", "没有开始游戏",
                $"至少需要 {RoomRules.MinMembers} 名成员有主战角色，现在只有 {fighters.Count} 名", "/我的角色"));
        }

        BattleResult battle = FunGameActionQueue.RunMix(
            [.. fighters.Select(x => x.Fighter)], new BattleOptions { MaxRound = 150, UseRoundRewards = false });
        CoreCharacter? winner = battle.Winner;

        List<string> lines =
        [
            $"- 房间：[ {room.Code} ] {RoomRules.Name(room.Kind)}　参战 {fighters.Count} 人",
            $"- 阵容：{string.Join("、", fighters.Select(x => x.Member.Display))}",
            winner is null
                ? $"- 结果：无人胜出（{battle.Rounds} 回合，打到终局审判）"
                : $"- 结果：{NameOf(fighters, winner)} 获胜（{battle.Rounds} 回合）",
            "- 生存状况："
        ];
        lines.AddRange(fighters
            .OrderByDescending(x => x.Fighter.HP / Math.Max(1, x.Fighter.MaxHP))
            .Select(x => $"  · {x.Member.Display}：HP {Math.Max(0, x.Fighter.HP):F0}/{x.Fighter.MaxHP:F0}"));
        lines.Add("- 战报摘要：");
        lines.AddRange(BattleTail(battle, 4));

        return new BattleReport(
            ActionResult.Success("⚔️ 混战结束",
                winner is null ? "平局" : $"{NameOf(fighters, winner)} 笑到最后",
                lines, ["/开始游戏", "/房间信息", "/退出房间"]),
            battle.Log);
    }

    private async Task<BattleReport> RunTeamRoomAsync(
        GameRoom room, IReadOnlyList<RoomMemberInfo> members, CancellationToken ct)
    {
        List<(RoomMemberInfo Member, CoreCharacter Fighter)> fighters = await LoadFightersAsync(members, ct);
        if (fighters.Count < RoomRules.MinMembers)
        {
            return BattleReport.Empty(ActionResult.Fail("参战角色不足", "没有开始游戏",
                $"至少需要 {RoomRules.MinMembers} 名成员有主战角色，现在只有 {fighters.Count} 名", "/我的角色"));
        }

        // 按加入顺序交替分队（旧版团队死斗没有实现分队规则，这里取最直观的均分方式）。
        List<CoreCharacter> red = [];
        List<CoreCharacter> blue = [];
        for (int i = 0; i < fighters.Count; i++)
        {
            (i % 2 == 0 ? red : blue).Add(fighters[i].Fighter);
        }
        if (red.Count == 0 || blue.Count == 0)
        {
            return BattleReport.Empty(ActionResult.Fail("无法分队", "没有开始游戏",
                "至少需要 2 名参战角色", "/房间信息"));
        }

        CoreTeam redTeam = new("红队", red);
        CoreTeam blueTeam = new("蓝队", blue);
        BattleResult battle = FunGameActionQueue.RunTeam([redTeam, blueTeam],
            new BattleOptions { MaxRound = 150, UseRoundRewards = false });
        CoreCharacter? winner = battle.Winner;

        string WinnerTeam(CoreCharacter winner)
        {
            (RoomMemberInfo Member, CoreCharacter Fighter) hit = fighters.First(x => ReferenceEquals(x.Fighter, winner));
            return red.Contains(hit.Fighter) ? "红队" : "蓝队";
        }

        List<string> lines =
        [
            $"- 房间：[ {room.Code} ] {RoomRules.Name(room.Kind)}　参战 {fighters.Count} 人",
            $"- 红队：{JoinTeam(red, fighters)}",
            $"- 蓝队：{JoinTeam(blue, fighters)}",
            winner is null
                ? $"- 结果：平局（{battle.Rounds} 回合）"
                : $"- 结果：{WinnerTeam(winner)} 获胜（{battle.Rounds} 回合）",
            "- 战报摘要："
        ];
        lines.AddRange(BattleTail(battle, 4));

        return new BattleReport(
            ActionResult.Success("⚔️ 团队死斗结束",
                winner is null ? "平局" : $"{WinnerTeam(winner)} 获胜",
                lines, ["/开始游戏", "/房间信息", "/退出房间"]),
            battle.Log);
    }

    private static string JoinTeam(List<CoreCharacter> team, List<(RoomMemberInfo Member, CoreCharacter Fighter)> fighters)
        => string.Join("、", team.Select(unit =>
            fighters.First(x => ReferenceEquals(x.Fighter, unit)).Member.Display));

    private static string NameOf(List<(RoomMemberInfo Member, CoreCharacter Fighter)> fighters, CoreCharacter winner)
    {
        RoomMemberInfo? member = fighters.FirstOrDefault(x => ReferenceEquals(x.Fighter, winner)).Member;
        return member is null ? winner.Name : $"{member.Display}（{winner.Name}）";
    }

    private static IEnumerable<string> BattleTail(BattleResult battle, int lines)
        => battle.Tail(lines)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => "  " + (line.Length > 120 ? line[..120] + "…" : line));

    private async Task<List<(RoomMemberInfo Member, CoreCharacter Fighter)>> LoadFightersAsync(
        IReadOnlyList<RoomMemberInfo> members, CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        List<(RoomMemberInfo Member, CoreCharacter Fighter)> result = [];
        foreach (RoomMemberInfo member in members)
        {
            PlayerCharacter? main = await PlayerService.MainCharacterAsync(db, member.UserId, ct);
            if (main is null) continue;
            CoreCharacter? fighter = CombatUnits.FromPlayer(main);
            if (fighter is null) continue;
            fighter.Name = $"{member.Display}｜{fighter.Name}";   // 同名角色也能在战报里区分
            result.Add((member, fighter));
        }
        return result;
    }

    private async Task<string> DisplayOfAsync(long userId, CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null) return $"旅人{userId}";
        return !string.IsNullOrWhiteSpace(user.Nickname) ? user.Nickname
            : !string.IsNullOrWhiteSpace(user.Handle) ? user.Handle
            : $"旅人{userId}";
    }

    private string DisplayOf(long userId)
        => _rooms.Values.SelectMany(x => x.Members)
            .FirstOrDefault(x => x.UserId == userId)?.Display ?? $"旅人{userId}";

    private void Sweep(DateTime now)
    {
        TimeSpan ttl = TimeSpan.FromMinutes(RoomRules.ExpireMinutes);
        List<string> expired =
        [
            .. _rooms.Values
                .Where(room => room.State == RoomState.Created && now - room.CreatedAtUtc > ttl)
                .Select(room => room.Code)
        ];
        foreach (string code in expired) RemoveRoom(code);
    }

    private void RemoveRoom(string code)
    {
        if (!_rooms.Remove(code, out GameRoom? room)) return;
        foreach (RoomMemberInfo member in room.Members) _userRoom.Remove(member.UserId);
        if (room.Kind == RoomKind.HorseRacing) _horseScopes.Remove(room.Scope);
    }

    private string NewCode()
    {
        while (true)
        {
            char[] chars = new char[RoomRules.CodeLength];
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = RoomRules.CodeAlphabet[random.Next(RoomRules.CodeAlphabet.Length)];
            }
            string code = new(chars);
            if (!_rooms.ContainsKey(code)) return code;
        }
    }

    private static string NormalizeCode(string? code)
        => string.IsNullOrWhiteSpace(code) ? "" : code.Trim().TrimStart('[').TrimEnd(']').Trim().ToUpperInvariant();

    private static string NormalizePassword(string? password)
        => string.IsNullOrWhiteSpace(password) ? "" : password.Trim();
}
