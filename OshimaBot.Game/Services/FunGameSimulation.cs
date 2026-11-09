using System.IO.Compression;
using System.Text;
using System.Text.Json;
using OshimaBot.Game.Constants;
using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Interface.Base;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.Framework;
using FunGame.Core.Model.Queue;
using OshimaBot.Content.Effects.OpenEffects;
using OshimaBot.Content.Skills;
using Milimoe.FunGameTesting.Others;

namespace OshimaBot.Game.Services
{
    public class FunGameSimulation
    {
        public static Dictionary<Character, CharacterStatistics> CharacterStatistics { get; } = [];
        public static Dictionary<Character, CharacterStatistics> TeamCharacterStatistics { get; } = [];
        public static IReadOnlyList<string> LastRecord { get; private set; } = [];
        public static IReadOnlyList<string> LastFullRecord { get; private set; } = [];
        public static GameMap Map { get; } = new FastAutoMap();

        public static void InitializeStatistics()
        {
            CharacterStatistics.Clear();
            TeamCharacterStatistics.Clear();
            LastRecord = [];
            LastFullRecord = [];

            foreach (Character character in FunGameService.Characters)
            {
                CharacterStatistics.Add(character, new());
                TeamCharacterStatistics.Add(character, new());
            }
        }
        public static bool IsRuning { get; set; } = false;
        public static bool IsWeb { get; set; } = false;
        public static bool IsDebug { get; set; } = false;
        public static bool PrintOut { get; set; } = false;
        public static bool DeathMatchRoundDetail { get; set; } = false;
        public static string Msg { get; set; } = "";

        /// <summary>
        /// 指定本局使用的随机种子；为 null 时每局自动随机<br/>
        /// 设值后（或读取 <see cref="Seed"/> 得到的上一局实际值）同一份输入数据下整局模拟可复现
        /// </summary>
        public static int? SeedOverride { get; set; } = null;

        /// <summary>
        /// 固定武器 Id（0 = 不固定，按品质随机）。由 <see cref="SimulationOptions.FixedWeaponId"/> 传入。
        /// <para/>用于平衡测试时消除「武器攻击力 20–170（8.5×）+ 副属性各异 + 武器类型倍率 1.41×」这组混淆变量。
        /// </summary>
        public static long FixedWeaponId { get; set; } = 0;

        /// <summary>
        /// 本局实际使用的随机种子（开局确定并保留，便于复现）
        /// </summary>
        public static int Seed { get; private set; } = 0;

        /// <summary>
        /// 模拟器自己的随机数生成器（由 <see cref="Seed"/> 决定）
        /// <para>队列创建之前的角色抽选、技能分配、分队等走它；队列创建之后一律走 <see cref="GamingQueue.Random"/>。</para>
        /// <para>两者同源，因此本文件内<b>禁止使用进程级的 <c>Random.Shared</c></b>，否则整局不可复现</para>
        /// </summary>
        public static Random Random { get; private set; } = new();

        public static async Task<List<string>> StartSimulationGame(SimulationOptions options)
        {
            PrintOut = options.PrintOut;
            IsWeb = options.IsWeb;
            DeathMatchRoundDetail = options.DeathMatchRoundDetail;

            // 展开为局部变量：方法体其余部分沿用旧参数名，保持最小改动
            bool isWeb = options.IsWeb;
            bool isTeam = options.IsTeam;
            bool deathMatchRoundDetail = options.DeathMatchRoundDetail;
            int maxRespawnTimesMix = options.MaxRespawnTimesMix;
            bool useStore = options.UseStore;
            bool hasMap = options.HasMap;
            bool isDebug = options.IsDebug;

            try
            {
                if (IsRuning) return ["游戏正在模拟中，请勿重复请求！"];

                List<string> result = [];
                List<string> lastRecord = [];
                Msg = "";

                IsRuning = true;

                // 本局种子：未指定则自动随机一次并保留实际值。
                // 局前随机（本类 Random）与局内随机（GamingQueue.Random）都由它派生，因此同种子整局可复现。
                Seed = SeedOverride ?? System.Random.Shared.Next();
                Random = new Random(Seed);

                // 固定武器（0 = 不固定）；平衡测试用，消除武器带来的攻击力/副属性/类型倍率变量
                FixedWeaponId = options.FixedWeaponId;

                // M = 0, W = 7, P1 = 1, P3 = 1
                // M = 1, W = 6, P1 = 2, P3 = 0
                // M = 2, W = 4, P1 = 0, P3 = 2
                // M = 2, W = 5, P1 = 0, P3 = 0
                // M = 3, W = 3, P1 = 1, P3 = 1
                // M = 4, W = 2, P1 = 2, P3 = 0
                // M = 5, W = 0, P1 = 0, P3 = 2
                // M = 5, W = 1, P1 = 0, P3 = 0

                List<Character> list = [.. FunGameService.Characters.Select(c => c.Copy())];

                if (list.Count > 10)
                {
                    if (PrintOut) Console.WriteLine();
                    if (PrintOut) Console.WriteLine("Start!!!");
                    if (PrintOut) Console.WriteLine();

                    List<Character> characters = [.. list.OrderBy(o => Random.Next()).Take(10)];

                    // 起始等级：默认 10 / 2 / 2（与旧版硬编码一致）；平衡测试可经 SimulationOptions 直接拉满
                    // 到 60 / 6 / 8，使实测口径对齐《数值设计手册·判定基准》的标尺（否则会被低等级阶段稀释）
                    int clevel = options.CharacterLevel > 0 ? options.CharacterLevel : 10;
                    int slevel = options.SkillLevel > 0 ? options.SkillLevel : 2;
                    int mlevel = options.NormalAttackLevel > 0 ? options.NormalAttackLevel : 2;

                    // 升级和赋能
                    for (int index = 0; index < characters.Count; index++)
                    {
                        Character c = characters[index];
                        c.Level = clevel;
                        c.NormalAttack.Level = mlevel;
                        FunGameService.AddCharacterSkills(c, 1, slevel, slevel);
                        foreach (Skill skillLoop in FunGameService.Skills.Where(s => s is not 疾走 && s is not 回复原状).OrderBy(o => Random.Next()).Take(3))
                        {
                            Skill skill = skillLoop.Copy();
                            skill.Character = c;
                            skill.Level = slevel;
                            c.Skills.Add(skill);
                        }
                        foreach (Skill skillLoop in FunGameService.CommonPassiveSkills.OrderBy(o => Random.Next()).Take(3))
                        {
                            Skill passive = skillLoop.Copy();
                            passive.Character = c;
                            passive.Level = 1;
                            c.Skills.Add(passive);
                        }
                        foreach (Skill skillLoop in FunGameService.CommonSuperSkills.OrderBy(o => Random.Next()).Take(3))
                        {
                            Skill super = skillLoop.Copy();
                            super.Character = c;
                            super.Level = slevel;
                            c.Skills.Add(super);
                        }
                        foreach (Effect e in c.Effects)
                        {
                            e.OnEffectLost(new HookContext(e.GamingQueue, c));
                        }
                    }

                    // 创建顺序表并排序（把本局种子交给队列：局内伤害浮动、AI 决策随机等全部由它派生）
                    GamingQueue actionQueue;
                    MixGamingQueue? mgq = null;
                    TeamGamingQueue? tgq = null;
                    if (isTeam)
                    {
                        tgq = new TeamGamingQueue(characters, WriteLine, seed: Seed)
                        {
                            MaxRespawnTimes = -1,
                            MaxScoreToWin = 30
                        };
                        actionQueue = tgq;
                    }
                    else
                    {
                        mgq = new MixGamingQueue(characters, WriteLine, seed: Seed)
                        {
                            MaxRespawnTimes = maxRespawnTimesMix
                        };
                        actionQueue = mgq;
                    }
                    if (hasMap) actionQueue.LoadGameMap(Map);
                    actionQueue.UseQueueProtected = false;
                    if (PrintOut) Console.WriteLine();

                    // 总游戏时长
                    double totalTime = 0;

                    // 初始化商店 团队模式专供
                    useStore = useStore && isTeam;
                    List<Item> store = [];
                    if (useStore)
                    {
                        AddStoreItems(actionQueue, store);
                    }

                    // 开始空投
                    Msg = "";
                    WriteLine($"[种子] 本局模拟使用随机种子 {Seed}（同种子可复现整局）。");
                    int mQuality = 0;
                    int wQuality = 0;
                    int aQuality = 0;
                    int sQuality = 0;
                    int acQuality = 0;
                    double nextDropTime = 0;
                    if (!useStore)
                    {
                        WriteLine($"社区送温暖了，现在随机发放空投！！");
                        DropItems(actionQueue, mQuality, wQuality, aQuality, sQuality, acQuality, false);
                        WriteLine("");
                        if (isWeb) result.Add("=== 空投 ===\r\n" + Msg);
                        nextDropTime = isTeam ? 90 : 60;
                        if (mQuality < 5)
                        {
                            mQuality++;
                        }
                        if (wQuality < 5)
                        {
                            wQuality++;
                        }
                        if (aQuality < 5)
                        {
                            aQuality++;
                        }
                        if (sQuality < 5)
                        {
                            sQuality++;
                        }
                        if (acQuality < 5)
                        {
                            acQuality++;
                        }
                    }

                    // 显示角色信息
                    if (PrintOut) characters.ForEach(c => Console.WriteLine(c.GetInfo()));

                    // 初始化队列，准备开始游戏
                    actionQueue.IsDebug = IsDebug || isDebug;
                    actionQueue.InitActionQueue();
                    actionQueue.SetCharactersToAIControl(false, characters);
                    if (PrintOut) Console.WriteLine();

                    // 团队模式
                    if (isTeam && actionQueue is TeamGamingQueue tg)
                    {
                        Msg = "=== 团队模式随机分组 ===\r\n\r\n";
                        // 打乱角色列表
                        List<Character> shuffledCharacters = [.. characters.OrderBy(c => actionQueue.Random.Next())];

                        // 创建两个团队
                        List<Character> group1 = [];
                        List<Character> group2 = [];

                        // 将角色交替分配到两个团队中
                        for (int cid = 0; cid < shuffledCharacters.Count; cid++)
                        {
                            // 创建角色的用户，用于绑定金币
                            User user = new()
                            {
                                Username = RandomNames.GenerateRandomChineseUserName(actionQueue.Random)
                            };
                            user.Inventory.Credits = 20;
                            Character thisCharacter = shuffledCharacters[cid];
                            //thisCharacter.User = user;

                            if (cid % 2 == 0)
                            {
                                group1.Add(thisCharacter);
                            }
                            else
                            {
                                group2.Add(thisCharacter);
                            }
                        }

                        // 添加到团队字典，第一个为队长
                        tg.AddTeam($"{group1.First()}的小队", group1);
                        tg.AddTeam($"{group2.First()}的小队", group2);

                        foreach (string team in tg.Teams.Keys)
                        {
                            WriteLine($"团队【{team}】的成员：\r\n{string.Join("\r\n", tg.Teams[team].Members)}\r\n");
                        }
                        result.Add(Msg);
                    }

                    // 显示初始顺序表
                    actionQueue.DisplayQueue();
                    if (PrintOut) Console.WriteLine();

                    actionQueue.CharacterDeathEvent += ActionQueue_CharacterDeath;

                    // 地图放置角色
                    if (actionQueue.Map != null)
                    {
                        GameMap map = actionQueue.Map;
                        HashSet<Grid> allocated = [];
                        List<Grid> allGrids = [.. map.Grids.Values];
                        if (isTeam && tgq != null)
                        {
                            Team team1 = tgq.Teams.Values.First();
                            Team team2 = tgq.Teams.Values.Last();

                            // 团队模式放置角色
                            List<Character> team1Members = [.. team1.Members];
                            List<Character> team2Members = [.. team2.Members];

                            // 获取地图尺寸
                            int mapLength = map.Length;
                            int mapWidth = map.Width;

                            // 放置队伍一在左下角区域
                            PlaceTeamInCorner(actionQueue, map, team1Members, allocated, startX: 0, endX: mapLength / 4, startY: mapWidth * 3 / 4, endY: mapWidth - 1);

                            // 放置队伍二在右上角区域
                            PlaceTeamInCorner(actionQueue, map, team2Members, allocated, startX: mapLength * 3 / 4, endX: mapLength - 1, startY: 0, endY: mapWidth / 4);
                        }
                        else
                        {
                            // 非团队模式，随机放置角色
                            foreach (Character character in characters)
                            {
                                character.NormalAttack.GamingQueue = actionQueue;
                                Grid grid = Grid.Empty;
                                do
                                {
                                    grid = allGrids[actionQueue.Random.Next(allGrids.Count)];
                                }
                                while (allocated.Contains(grid));
                                allocated.Add(grid);
                                map.SetCharacterCurrentGrid(character, grid);
                            }
                        }
                    }

                    // 总回合数
                    int maxRound = 9999;

                    // 随机回合奖励（特效池已迁入模组公共区域 RoundRewardPool）
                    // 只取一次随机结果并存下来：Keys 与取值必须来自同一份实例
                    Dictionary<EffectID, Dictionary<string, object>> roundRewards = RoundRewardPool.Create(actionQueue.Random);
                    Dictionary<long, bool> effects = RoundRewardPool.BuildEffectMap(roundRewards.Keys);
                    actionQueue.InitRoundRewards(effects, options.BindToCharacter, id => roundRewards[(EffectID)id]);

                    int i = 1;
                    while (i < maxRound)
                    {
                        if (i != 1)
                        {
                            lastRecord.Add(actionQueue.LastRound.ToString());
                        }

                        Msg = "";
                        if (i == maxRound - 1)
                        {
                            if (isTeam)
                            {
                                WriteLine("两队打到天昏地暗，流局了！！");
                                break;
                            }
                            else
                            {
                                WriteLine($"=== 终局审判 ===");
                                Dictionary<Character, double> 他们的血量百分比 = [];
                                foreach (Character c in characters)
                                {
                                    他们的血量百分比.TryAdd(c, c.HP / c.MaxHP);
                                }
                                double max = 他们的血量百分比.Values.Max();
                                Character winner = 他们的血量百分比.Keys.Where(c => 他们的血量百分比[c] == max).First();
                                WriteLine("[ " + winner + " ] 成为了天选之人！！");
                                foreach (Character c in characters.Where(c => c != winner && c.HP > 0))
                                {
                                    WriteLine("[ " + winner + " ] 对 [ " + c + " ] 造成了 99999999999 点真实伤害。");
                                    actionQueue.DeathCalculation(winner, c);
                                }
                                result.Add(Msg);
                                mgq?.EndGameInfo(winner);
                                break;
                            }
                        }

                        // 检查是否有角色可以行动
                        Character? characterToAct = actionQueue.NextCharacter();

                        // 处理回合
                        if (characterToAct != null)
                        {
                            WriteLine($"=== Round {i++} [ Time: {totalTime} ] ===");
                            WriteLine("现在是 [ " + characterToAct + (tgq != null ? "（" + (tgq.GetTeam(characterToAct)?.Name ?? "") + "）" : "") + " ] 的回合！");

                            bool isGameEnd = actionQueue.ProcessTurn(characterToAct);

                            if (isGameEnd)
                            {
                                result.Add(Msg);
                                lastRecord.Add(actionQueue.LastRound.ToString());
                                break;
                            }

                            if (isWeb) actionQueue.DisplayQueue();
                            WriteLine("");
                            if (actionQueue.Map != null)
                            {
                                WriteLine(DisplayMapInConsole(actionQueue.Map));
                                WriteLine("");
                            }
                        }

                        string roundMsg = "";
                        if (actionQueue.LastRound.HasKill)
                        {
                            roundMsg = Msg;
                            if (!deathMatchRoundDetail)
                            {
                                roundMsg = actionQueue.LastRound.ToString().Trim() + $"\r\n{(tgq != null ? $"比分：{string.Join(" / ", tgq.Teams.Values.Select(t => $"{t.Name}({t.Score})"))}，击杀来自{tgq.GetTeam(tgq.LastRound.Actor)}" : "")}\r\n";
                            }
                            Msg = "";
                        }

                        // 模拟时间流逝
                        double timeLapse = actionQueue.TimeLapse();
                        totalTime = actionQueue.TotalTime;
                        nextDropTime -= timeLapse;
                        //Thread.Sleep(1);

                        if (actionQueue.GameOver)
                        {
                            result.Add(Msg);
                            lastRecord.Add(actionQueue.LastRound.ToString());
                            break;
                        }

                        if (roundMsg != "")
                        {
                            if ((isTeam && deathMatchRoundDetail || !isTeam) && isWeb)
                            {
                                roundMsg += "\r\n" + Msg;
                            }
                            result.Add(roundMsg);
                        }

                        // 模拟商店购买
                        if (useStore)
                        {
                            // 发钱了
                            foreach (Character character in actionQueue.HardnessTime.Keys)
                            {
                                character.User.Inventory.Credits += 3 * timeLapse;
                            }
                            BuyItems(actionQueue, store);
                        }

                        // 周期空投：EnablePeriodicDrop=false 时跳过，只保留开局那次（DropItems(..., addLevel: false)），
                        // 使装备与技能池在整局内恒定，便于测量「技能在定态下的收益」
                        if (!useStore && options.EnablePeriodicDrop && nextDropTime <= 0)
                        {
                            // 空投
                            Msg = "";
                            WriteLine($"社区送温暖了，现在随机发放空投！！");
                            DropItems(actionQueue, mQuality, wQuality, aQuality, sQuality, acQuality);
                            WriteLine("");
                            if (isWeb) result.Add("=== 空投 ===\r\n" + Msg);
                            nextDropTime = 40;
                            if (mQuality <= 5)
                            {
                                mQuality++;
                            }
                            if (wQuality <= 5)
                            {
                                wQuality++;
                            }
                            if (aQuality <= 5)
                            {
                                aQuality++;
                            }
                            if (sQuality <= 5)
                            {
                                sQuality++;
                            }
                            if (acQuality <= 5)
                            {
                                acQuality++;
                            }
                        }
                    }

                    if (PrintOut)
                    {
                        Console.WriteLine("--- End ---");
                        Console.WriteLine($"总游戏时长：{totalTime:0.##}");
                        Console.WriteLine("");
                    }

                    // 赛后统计
                    FunGameService.GetCharacterRating(actionQueue.CharacterStatistics, isTeam, tgq != null ? tgq.EliminatedTeams : []);
                    actionQueue.Rounds.Last().CharacterStatistics = actionQueue.CharacterStatistics;

                    // 统计技术得分，评选 MVP
                    Character? mvp = actionQueue.CharacterStatistics.OrderByDescending(d => d.Value.Rating).Select(d => d.Key).FirstOrDefault();
                    StringBuilder mvpBuilder = new();
                    if (mvp != null)
                    {
                        CharacterStatistics stats = actionQueue.CharacterStatistics[mvp];
                        stats.MVPs++;
                        mvpBuilder.AppendLine($"{(tgq != null ? "[ " + tgq.GetTeamFromEliminated(mvp)?.Name + " ] " : "")}[ {mvp.ToStringWithLevel()} ]");
                        mvpBuilder.AppendLine($"技术得分：{stats.Rating:0.0#} / 击杀数：{stats.Kills} / 助攻数：{stats.Assists}{(actionQueue.MaxRespawnTimes != 0 ? " / 死亡数：" + stats.Deaths : "")}");
                        mvpBuilder.AppendLine($"存活时长：{stats.LiveTime:0.##} / 存活回合数：{stats.LiveRound} / 行动回合数：{stats.ActionTurn} / 总计决策数：{stats.TurnDecisions} / 总计决策点：{stats.UseDecisionPoints}");
                        mvpBuilder.AppendLine($"控制时长：{stats.ControlTime:0.##} / 总计治疗：{stats.TotalHeal:0.##} / 护盾抵消：{stats.TotalShield:0.##}");
                        mvpBuilder.AppendLine($"总计伤害：{stats.TotalDamage:0.##} / 总计物理伤害：{stats.TotalPhysicalDamage:0.##} / 总计魔法伤害：{stats.TotalMagicDamage:0.##}");
                        mvpBuilder.AppendLine($"总承受伤害：{stats.TotalTakenDamage:0.##} / 总承受物理伤害：{stats.TotalTakenPhysicalDamage:0.##} / 总承受魔法伤害：{stats.TotalTakenMagicDamage:0.##}");
                        if (stats.TotalTrueDamage > 0 || stats.TotalTakenTrueDamage > 0) mvpBuilder.AppendLine($"总计真实伤害：{stats.TotalTrueDamage:0.##} / 总承受真实伤害：{stats.TotalTakenTrueDamage:0.##}");
                        mvpBuilder.AppendLine($"每秒伤害：{stats.DamagePerSecond:0.##} / 每回合伤害：{stats.DamagePerTurn:0.##}");
                        if (actionQueue.IsDebug) WriteLine(mvp.GetInfo());
                    }

                    int top = isWeb ? actionQueue.CharacterStatistics.Count : 0; // 回执多少个角色的统计信息
                    int count = 1;
                    if (isWeb)
                    {
                        WriteLine("=== 技术得分排行榜 ==="); // 这是输出在界面上的
                        Msg = $"=== 技术得分排行榜 TOP{top} ===\r\n"; // 这个是下一条给Result回执的标题，覆盖掉上面方法里的赋值了
                    }
                    else
                    {
                        StringBuilder ratingBuilder = new();
                        if (tgq != null)
                        {
                            ratingBuilder.AppendLine($"=== 赛后数据 ===");
                            foreach (Team team in tgq.EliminatedTeams)
                            {
                                ratingBuilder.AppendLine($"☆--- [ {team} ] ---☆");
                                foreach (Character character in tgq.CharacterStatistics.Where(d => team.Members.Contains(d.Key))
                                    .OrderByDescending(d => d.Value.Rating).Select(d => d.Key))
                                {
                                    CharacterStatistics stats = tgq.CharacterStatistics[character];
                                    ratingBuilder.AppendLine($"[ {stats.Rating:0.0#} ]  {character}（{stats.Kills} / {stats.Assists} / {stats.Deaths}）");
                                }
                            }
                        }
                        else if (mgq != null)
                        {
                            ratingBuilder.AppendLine($"=== 赛后数据 ===");
                            foreach (Character statCharacter in actionQueue.CharacterStatistics
                                .OrderBy(kv => kv.Value.Deaths)
                                .ThenByDescending(kv => kv.Value.Rating)
                                .ThenByDescending(kv => kv.Value.Kills).Select(kv => kv.Key))
                            {
                                CharacterStatistics stats = actionQueue.CharacterStatistics[statCharacter];
                                ratingBuilder.AppendLine($"[ {stats.Rating:0.0#} ]  {statCharacter}（{stats.Kills} / {stats.Assists} / {stats.Deaths}）");
                            }
                        }
                        WriteLine("=== 本场比赛最佳角色 ===");
                        Msg = $"=== 本场比赛最佳角色 ===\r\n";
                        WriteLine(mvpBuilder.ToString() + "\r\n\r\n" + ratingBuilder.ToString());

                        if (PrintOut)
                        {
                            Console.WriteLine();
                            Console.WriteLine("=== 技术得分排行榜 ===");
                        }
                    }

                    if (tgq != null)
                    {
                        foreach (Character character in tgq.CharacterStatistics.OrderByDescending(d => d.Value.Rating).Select(d => d.Key))
                        {
                            StringBuilder builder = new();
                            CharacterStatistics stats = tgq.CharacterStatistics[character];
                            builder.AppendLine($"{(isWeb ? count + "." : ("[ " + tgq.GetTeamFromEliminated(character)?.Name + " ]" ?? ""))} [ {character.ToStringWithLevel()} ]");
                            builder.AppendLine($"技术得分：{stats.Rating:0.0#} / 击杀数：{stats.Kills} / 助攻数：{stats.Assists}{(tgq.MaxRespawnTimes != 0 ? " / 死亡数：" + stats.Deaths : "")}");
                            builder.AppendLine($"存活时长：{stats.LiveTime:0.##} / 存活回合数：{stats.LiveRound} / 行动回合数：{stats.ActionTurn} / 总计决策数：{stats.TurnDecisions} / 总计决策点：{stats.UseDecisionPoints}");
                            builder.AppendLine($"控制时长：{stats.ControlTime:0.##} / 总计治疗：{stats.TotalHeal:0.##} / 护盾抵消：{stats.TotalShield:0.##}");
                            builder.AppendLine($"总计伤害：{stats.TotalDamage:0.##} / 总计物理伤害：{stats.TotalPhysicalDamage:0.##} / 总计魔法伤害：{stats.TotalMagicDamage:0.##}");
                            builder.AppendLine($"总承受伤害：{stats.TotalTakenDamage:0.##} / 总承受物理伤害：{stats.TotalTakenPhysicalDamage:0.##} / 总承受魔法伤害：{stats.TotalTakenMagicDamage:0.##}");
                            if (stats.TotalTrueDamage > 0 || stats.TotalTakenTrueDamage > 0) builder.AppendLine($"总计真实伤害：{stats.TotalTrueDamage:0.##} / 总承受真实伤害：{stats.TotalTakenTrueDamage:0.##}");
                            builder.Append($"每秒伤害：{stats.DamagePerSecond:0.##} / 每回合伤害：{stats.DamagePerTurn:0.##}");
                            if (count++ <= top)
                            {
                                WriteLine(builder.ToString());
                            }
                            else
                            {
                                if (PrintOut) Console.WriteLine(builder.ToString());
                            }

                            if (!useStore)
                            {
                                CharacterStatistics? totalStats = TeamCharacterStatistics.Where(kv => kv.Key.GetName() == character.GetName()).Select(kv => kv.Value).FirstOrDefault();
                                if (totalStats != null)
                                {
                                    UpdateStatistics(totalStats, stats);
                                }
                            }
                        }
                    }
                    else
                    {
                        foreach (Character character in actionQueue.CharacterStatistics.OrderByDescending(d => d.Value.Rating).Select(d => d.Key))
                        {
                            StringBuilder builder = new();
                            CharacterStatistics stats = actionQueue.CharacterStatistics[character];
                            builder.AppendLine($"{(isWeb ? count + ". " : "")}[ {character.ToStringWithLevel()} ]");
                            builder.AppendLine($"技术得分：{stats.Rating:0.0#} / 击杀数：{stats.Kills} / 助攻数：{stats.Assists}{(actionQueue.MaxRespawnTimes != 0 ? " / 死亡数：" + stats.Deaths : "")}");
                            builder.AppendLine($"存活时长：{stats.LiveTime:0.##} / 存活回合数：{stats.LiveRound} / 行动回合数：{stats.ActionTurn} / 总计决策数：{stats.TurnDecisions} / 总计决策点：{stats.UseDecisionPoints}");
                            builder.AppendLine($"控制时长：{stats.ControlTime:0.##} / 总计治疗：{stats.TotalHeal:0.##} / 护盾抵消：{stats.TotalShield:0.##}");
                            builder.AppendLine($"总计伤害：{stats.TotalDamage:0.##} / 总计物理伤害：{stats.TotalPhysicalDamage:0.##} / 总计魔法伤害：{stats.TotalMagicDamage:0.##}");
                            builder.AppendLine($"总承受伤害：{stats.TotalTakenDamage:0.##} / 总承受物理伤害：{stats.TotalTakenPhysicalDamage:0.##} / 总承受魔法伤害：{stats.TotalTakenMagicDamage:0.##}");
                            if (stats.TotalTrueDamage > 0 || stats.TotalTakenTrueDamage > 0) builder.AppendLine($"总计真实伤害：{stats.TotalTrueDamage:0.##} / 总承受真实伤害：{stats.TotalTakenTrueDamage:0.##}");
                            builder.Append($"每秒伤害：{stats.DamagePerSecond:0.##} / 每回合伤害：{stats.DamagePerTurn:0.##}");
                            if (count++ <= top)
                            {
                                WriteLine(builder.ToString());
                            }
                            else
                            {
                                if (PrintOut) Console.WriteLine(builder.ToString());
                            }

                            if (!useStore)
                            {
                                CharacterStatistics? totalStats = CharacterStatistics.Where(kv => kv.Key.GetName() == character.GetName()).Select(kv => kv.Value).FirstOrDefault();
                                if (totalStats != null)
                                {
                                    UpdateStatistics(totalStats, stats);
                                }
                            }
                        }
                    }

                    result.Add(Msg);
                    lastRecord.Add(Msg);

                    // 显示每个角色的信息
                    if (isWeb)
                    {
                        if (tgq != null)
                        {
                            top = 1;
                            for (i = tgq.EliminatedTeams.Count - 1; i >= 0; i--)
                            {
                                Team team = tgq.EliminatedTeams[i];
                                string topTeam = "";
                                if (top == 1)
                                {
                                    topTeam = "冠军";
                                }
                                if (top == 2)
                                {
                                    topTeam = "亚军";
                                }
                                if (top == 3)
                                {
                                    topTeam = "季军";
                                }
                                if (top > 3)
                                {
                                    topTeam = $"第 {top} 名";
                                }
                                foreach (Character character in team.Members)
                                {
                                    string msg = $"== {topTeam}团队 [ {team.Name} ] ==\r\n== 角色：[ {character} ] ==\r\n{character.GetInfo()}";
                                    result.Add(msg);
                                    lastRecord.Add(msg);
                                }
                                top++;
                            }
                        }
                        else
                        {
                            for (i = actionQueue.Eliminated.Count - 1; i >= 0; i--)
                            {
                                Character character = actionQueue.Eliminated[i];
                                string msg = $"=== 角色 [ {character} ] ===\r\n{character.GetInfo()}";
                                result.Add(msg);
                                lastRecord.Add(msg);
                            }
                        }
                    }

                    string zipFileName = "rounds_archive.zip";
                    try
                    {
                        WriteRoundsToZip(actionQueue.Rounds.ToDictionary(kv => kv.Round, kv => kv), zipFileName);
                    }
                    catch (Exception ex)
                    {
                        // 档案落盘失败（IO/权限/磁盘满等）不应连带丢弃整局模拟结果
                        string msg = $"回合记录写入失败（{zipFileName}）：{ex.Message}";
                        Console.WriteLine(ex);
                        result.Add(msg);
                        lastRecord.Add(msg);
                    }

                    LastRecord = result;
                    LastFullRecord = lastRecord;
                    IsRuning = false;
                }

                return result;
            }
            catch (Exception ex)
            {
                IsRuning = false;
                Console.WriteLine(ex);
                return [ex.ToString()];
            }
        }

        private static bool ActionQueue_CharacterDeath(DeathContext ctx)
        {
            if (ctx.Trigger is not Character death) return true;
            death.Items.Clear();
            return true;
        }

        /// <summary>
        /// 将回合记录字典序列化为 JSON，然后压缩并写入 ZIP 文件
        /// <para/>
        /// 逐回合容错：单个回合序列化失败（例如历史版本因同名技能重复键抛出的 ArgumentException）
        /// 只跳过该回合，并把原因写入同档案的 <c>rounds_skipped.json</c>，其余回合与整局结果不受影响。
        /// 逐回合序列化到字符串后再 <see cref="Utf8JsonWriter.WriteRawValue"/> 输出，
        /// 保证失败回合不会在主 JSON 里留下半截对象；内存里同时只保留一个回合的 JSON。
        /// </summary>
        /// <param name="roundsData">要写入的字典数据</param>
        /// <param name="zipFilePath">输出的 ZIP 文件路径</param>
        /// <returns>被跳过的回合及其原因（未跳过时为空列表）</returns>
        public static List<(int Round, string Error)> WriteRoundsToZip(Dictionary<int, RoundRecord> roundsData, string zipFilePath)
        {
            // 确保目标目录存在
            string? directory = Path.GetDirectoryName(zipFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            List<(int Round, string Error)> skipped = [];

            // 创建 ZIP 文件
            using FileStream zipFileStream = new(zipFilePath, FileMode.Create);
            using ZipArchive zipArchive = new(zipFileStream, ZipArchiveMode.Create);
            // 在 ZIP 档案中创建一个条目来存放 JSON 数据
            ZipArchiveEntry jsonEntry = zipArchive.CreateEntry("rounds_data.json", CompressionLevel.Optimal);

            // 手动写出字典外壳，逐回合序列化：坏回合可以单独跳过
            using (Stream entryStream = jsonEntry.Open())
            {
                using Utf8JsonWriter writer = new(entryStream);
                writer.WriteStartObject();
                foreach (KeyValuePair<int, RoundRecord> kv in roundsData)
                {
                    string roundJson;
                    try
                    {
                        roundJson = JsonSerializer.Serialize(kv.Value, JsonTool.JsonSerializerOptions);
                    }
                    catch (Exception ex)
                    {
                        skipped.Add((kv.Key, $"{ex.GetType().Name}: {ex.Message}"));
                        Console.WriteLine($"[警告] 第 {kv.Key} 回合序列化失败，已跳过：{ex.Message}");
                        continue;
                    }

                    writer.WritePropertyName(kv.Key.ToString());
                    // 内容由 System.Text.Json 自己产出，必然是合法 JSON，跳过二次校验
                    writer.WriteRawValue(roundJson, skipInputValidation: true);
                }
                writer.WriteEndObject();
            }

            if (skipped.Count > 0)
            {
                ZipArchiveEntry skippedEntry = zipArchive.CreateEntry("rounds_skipped.json", CompressionLevel.Optimal);
                using Stream skippedStream = skippedEntry.Open();
                using Utf8JsonWriter skippedWriter = new(skippedStream, new JsonWriterOptions { Indented = true });
                skippedWriter.WriteStartArray();
                foreach ((int round, string error) in skipped)
                {
                    skippedWriter.WriteStartObject();
                    skippedWriter.WriteNumber("Round", round);
                    skippedWriter.WriteString("Error", error);
                    skippedWriter.WriteEndObject();
                }
                skippedWriter.WriteEndArray();

                Console.WriteLine($"回合记录已写入到: {zipFilePath}，其中 {skipped.Count} 个回合因序列化失败被跳过（详见 rounds_skipped.json）。");
            }
            else
            {
                Console.WriteLine($"回合记录已成功序列化、压缩并写入到: {zipFilePath}");
            }

            return skipped;
        }

        /// <summary>
        /// 从 ZIP 文件中读取并解压 JSON 数据，然后反序列化为回合记录字典
        /// </summary>
        /// <param name="zipFilePath">输入的 ZIP 文件路径</param>
        /// <returns>反序列化后的字典数据</returns>
        public static Dictionary<int, RoundRecord>? ReadRoundsFromZip(string zipFilePath)
        {
            if (!File.Exists(zipFilePath))
            {
                Console.WriteLine($"错误: 文件不存在 {zipFilePath}");
                return null;
            }

            Dictionary<int, RoundRecord>? roundsData;

            // 打开 ZIP 文件
            using FileStream? zipFileStream = new(zipFilePath, FileMode.Open);
            using ZipArchive? zipArchive = new(zipFileStream, ZipArchiveMode.Read);
            ZipArchiveEntry? jsonEntry = zipArchive.GetEntry("rounds_data.json");

            if (jsonEntry == null)
            {
                Console.WriteLine($"错误: ZIP 档案中找不到 'rounds_data.json' 条目。");
                return null;
            }

            // 获取条目的流，从这个流中读取 JSON 数据
            using Stream? entryStream = jsonEntry.Open();
            try
            {
                roundsData = JsonSerializer.Deserialize<Dictionary<int, RoundRecord>>(entryStream, JsonTool.JsonSerializerOptions);
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"JSON 反序列化错误: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"读取或反序列化过程中发生未知错误: {ex.Message}");
                return null;
            }

            Console.WriteLine($"回合记录已成功从 {zipFilePath} 读取、解压并反序列化。");
            return roundsData;
        }

        public static void WriteLine(string str)
        {
            Msg += str + "\r\n";
            if (PrintOut) Console.WriteLine(str);
        }

        public static void AddStoreItems(GamingQueue queue, List<Item> store)
        {
            foreach (Item item in FunGameService.Equipment)
            {
                Item realItem = item.Copy();
                realItem.SetGamingQueue(queue);
                realItem.Price = queue.Random.Next(1, 10) * ((int)item.QualityType + 1) * 2;
                store.Add(realItem);
            }
        }

        public static void BuyItems(GamingQueue queue, List<Item> store)
        {
            // 升级成本
            double costLevel = 20;

            // 卡包购买/升级成本
            Dictionary<QualityType, double> costMCP = new()
            {
                { QualityType.White, 5 }, // 普通
                { QualityType.Green, 10 }, // 优秀
                { QualityType.Blue, 15 }, // 稀有
                { QualityType.Purple, 20 }, // 史诗
                { QualityType.Orange, 25 }, // 传说
            };

            foreach (Character character in queue.HardnessTime.Keys)
            {
                // 购买欲望，可以加多个判断
                List<Func<bool>> funcs = [
                    () => queue.Random.NextDouble() > 0.3
                ];

                if (funcs.All(f => f()))
                {
                    // 定义操作的概率（优先级）
                    Dictionary<string, double> pOperations = new()
                    {
                        { "升级角色", 0.8 }, // 会同时升级技能等级，优先级高
                        { "买卡包", 0.6 }, // 会获得属性加成和魔法技能，优先级高
                        { "买武器", 0.4 }, // 提升攻击力
                        { "买防具", 0.4 }, // 提升防御性能
                        { "买鞋子", 0.4 }, // 提升速度
                        { "买饰品", 0.4 } // 提升综合能力
                    };

                    // 记录操作
                    Dictionary<string, bool> operation = new()
                    {
                        { "升级角色", false },
                        { "买卡包", false },
                        { "买武器", false },
                        { "买防具", false },
                        { "买鞋子", false },
                        { "买饰品", false }
                    };

                    // 本次购买只提升？
                    bool onlyLarger = queue.Random.NextDouble() > 0.3;

                    bool buy = true;
                    int failedBuyTimes = 0;
                    while (buy && pOperations.Values.Any(o => o != 0) && operation.Values.Any(o => o == false))
                    {
                        // 先看手上的钱能买什么
                        IEnumerable<Item> canBuys = store.Where(i => i.Price <= character.User.Inventory.Credits);

                        // 然后看能买的东西里，是否品质符合
                        Item[] weapons = [.. canBuys.Where(i => i.ItemType == ItemType.Weapon &&
                            (onlyLarger ? (int)i.QualityType > Convert.ToInt32(character.EquipSlot.Weapon?.QualityType) : (int)i.QualityType >= Convert.ToInt32(character.EquipSlot.Weapon?.QualityType)))];
                        Item[] armors = [.. canBuys.Where(i => i.ItemType == ItemType.Armor &&
                            (onlyLarger ? (int)i.QualityType > Convert.ToInt32(character.EquipSlot.Armor?.QualityType) : (int)i.QualityType >= Convert.ToInt32(character.EquipSlot.Armor?.QualityType)))];
                        Item[] shoes = [.. canBuys.Where(i => i.ItemType == ItemType.Shoes &&
                            (onlyLarger ? (int)i.QualityType > Convert.ToInt32(character.EquipSlot.Shoes?.QualityType) : (int)i.QualityType >= Convert.ToInt32(character.EquipSlot.Shoes?.QualityType)))];
                        Item[] accessories = [.. canBuys.Where(i => i.ItemType == ItemType.Accessory &&
                            (onlyLarger ? (int)i.QualityType > Math.Min(Convert.ToInt32(character.EquipSlot.Accessory1?.QualityType), Convert.ToInt32(character.EquipSlot.Accessory2?.QualityType))
                            : (int)i.QualityType >= Math.Min(Convert.ToInt32(character.EquipSlot.Accessory1?.QualityType), Convert.ToInt32(character.EquipSlot.Accessory2?.QualityType))))];

                        // 魔法卡包单独列出
                        int mQuality = Convert.ToInt32(character.EquipSlot.MagicCardPack?.QualityType);

                        if (character.User.Inventory.Credits < costLevel)
                        {
                            pOperations["升级角色"] = 0;
                        }

                        if (character.User.Inventory.Credits < costMCP[(QualityType)mQuality])
                        {
                            pOperations["买卡包"] = 0;
                        }

                        if (weapons.Length == 0)
                        {
                            pOperations["买武器"] = 0;
                        }

                        if (armors.Length == 0)
                        {
                            pOperations["买防具"] = 0;
                        }

                        if (shoes.Length == 0)
                        {
                            pOperations["买鞋子"] = 0;
                        }

                        if (accessories.Length == 0)
                        {
                            pOperations["买饰品"] = 0;
                        }

                        double p = queue.Random.NextDouble();

                        foreach (KeyValuePair<string, double> kvp in pOperations.OrderByDescending(kv => kv.Value))
                        {
                            string op = "";
                            if (kvp.Value != 0 && p <= kvp.Value)
                            {
                                op = kvp.Key;
                            }
                            else
                            {
                                continue;
                            }
                            switch (op)
                            {
                                case "升级角色":
                                    // 钱够吗？
                                    if (character.User.Inventory.Credits >= costLevel)
                                    {
                                        character.User.Inventory.Credits -= costLevel;
                                        operation["升级角色"] = true;
                                        character.SetLevel(character.Level + 10, false);
                                        character.NormalAttack.Level += 2;
                                        foreach (Skill skill in character.Skills)
                                        {
                                            if (skill.SkillType == SkillType.Passive) continue;
                                            skill.Level += 2;
                                        }
                                        Character? original = queue.Original[character.Guid];
                                        if (original != null)
                                        {
                                            original.Level += 10;
                                            original.NormalAttack.Level += 2;
                                            foreach (Skill skill in original.Skills)
                                            {
                                                if (skill.SkillType == SkillType.Passive) continue;
                                                skill.Level += 2;
                                            }
                                        }
                                        WriteLine($"[ {character} ] 消费了 {costLevel} {General.GameplayEquilibriumConstant.InGameCurrency}，购买了【升级角色】！当前角色 {character.Level} 级，技能 {character.NormalAttack.Level} 级（战技、爆发技最高 6 级）。");
                                    }
                                    else failedBuyTimes++;
                                    break;
                                case "买卡包":
                                    // 看角色能购买的最高级卡包是什么
                                    QualityType canBuyMCP = costMCP.Where(kv => (int)kv.Key >= mQuality && character.User.Inventory.Credits >= kv.Value).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();
                                    double mcpCost = costMCP[canBuyMCP];
                                    // 买卡包
                                    if (character.User.Inventory.Credits >= mcpCost)
                                    {
                                        character.User.Inventory.Credits -= mcpCost;
                                        operation["买卡包"] = true;
                                        Item? mcp = FunGameService.GenerateCoreMagicCardPack(queue.Random, 3, canBuyMCP);
                                        if (mcp != null)
                                        {
                                            foreach (Skill magic in mcp.Skills.Magics)
                                            {
                                                magic.Level = character.NormalAttack.Level;
                                            }
                                            mcp.SetGamingQueue(queue);
                                            WriteLine($"[ {character} ] 消费了 {mcpCost} {General.GameplayEquilibriumConstant.InGameCurrency}，购买了【卡包-{mcp.Name}】！详细说明：\r\n{mcp}");
                                            queue.Equip(character, mcp);
                                        }
                                    }
                                    else failedBuyTimes++;
                                    break;
                                case "买武器":
                                    Item weapon = weapons[queue.Random.Next(weapons.Length)];
                                    double wCost = weapon.Price;
                                    if (character.User.Inventory.Credits >= wCost)
                                    {
                                        character.User.Inventory.Credits -= wCost;
                                        operation["买武器"] = true;
                                        Item realItem = weapon.Copy();
                                        realItem.SetGamingQueue(queue);
                                        WriteLine($"[ {character} ] 消费了 {wCost} {General.GameplayEquilibriumConstant.InGameCurrency}，购买了武器-{realItem.Name}！详细说明：\r\n{realItem}");
                                        queue.Equip(character, realItem);
                                    }
                                    else failedBuyTimes++;
                                    break;
                                case "买防具":
                                    Item armor = armors[queue.Random.Next(armors.Length)];
                                    double aCost = armor.Price;
                                    if (character.User.Inventory.Credits >= aCost)
                                    {
                                        character.User.Inventory.Credits -= aCost;
                                        operation["买防具"] = true;
                                        Item realItem = armor.Copy();
                                        realItem.SetGamingQueue(queue);
                                        WriteLine($"[ {character} ] 消费了 {aCost} {General.GameplayEquilibriumConstant.InGameCurrency}，购买了防具-{realItem.Name}！详细说明：\r\n{realItem}");
                                        queue.Equip(character, realItem);
                                    }
                                    else failedBuyTimes++;
                                    break;
                                case "买鞋子":
                                    Item shoe = shoes[queue.Random.Next(shoes.Length)];
                                    double sCost = shoe.Price;
                                    if (character.User.Inventory.Credits >= sCost)
                                    {
                                        character.User.Inventory.Credits -= sCost;
                                        operation["买鞋子"] = true;
                                        Item realItem = shoe.Copy();
                                        realItem.SetGamingQueue(queue);
                                        WriteLine($"[ {character} ] 消费了 {sCost} {General.GameplayEquilibriumConstant.InGameCurrency}，购买了鞋子-{realItem.Name}！详细说明：\r\n{realItem}");
                                        queue.Equip(character, realItem);
                                    }
                                    else failedBuyTimes++;
                                    break;
                                case "买饰品":
                                    Item accessory = accessories[queue.Random.Next(accessories.Length)];
                                    double acCost = accessory.Price;
                                    if (character.User.Inventory.Credits >= acCost)
                                    {
                                        character.User.Inventory.Credits -= acCost;
                                        operation["买饰品"] = true;
                                        Item realItem = accessory.Copy();
                                        realItem.SetGamingQueue(queue);
                                        WriteLine($"[ {character} ] 消费了 {acCost} {General.GameplayEquilibriumConstant.InGameCurrency}，购买了饰品-{realItem.Name}！详细说明：\r\n{realItem}");
                                        queue.Equip(character, realItem);
                                    }
                                    else failedBuyTimes++;
                                    break;
                                default:
                                    break;
                            }
                        }
                        // 看还要不要买，超过两次购买失败说明没钱别买了
                        buy = failedBuyTimes < 2 && funcs.All(f => f());
                    }
                    WriteLine($"[ {character} ] 结束购买，剩余{General.GameplayEquilibriumConstant.InGameCurrency}：{character.User.Inventory.Credits:0.00}。");
                }
            }
        }

        public static void DropItems(GamingQueue queue, int mQuality, int wQuality, int aQuality, int sQuality, int acQuality, bool addLevel = true)
        {
            Item[] weapons = [.. FunGameService.Equipment.Where(i => i.ItemType == ItemType.Weapon && (wQuality >= 5 && (int)i.QualityType >= 5 || wQuality < 5 && (int)i.QualityType == wQuality))];
            Item[] armors = [.. FunGameService.Equipment.Where(i => i.ItemType == ItemType.Armor && (aQuality >= 5 && (int)i.QualityType >= 5 || aQuality < 5 && (int)i.QualityType == aQuality))];
            Item[] shoes = [.. FunGameService.Equipment.Where(i => i.ItemType == ItemType.Shoes && (sQuality >= 5 && (int)i.QualityType >= 5 || sQuality < 5 && (int)i.QualityType == sQuality))];
            Item[] accessories = [.. FunGameService.Equipment.Where(i => i.ItemType == ItemType.Accessory && (acQuality >= 5 && (int)i.QualityType >= 5 || acQuality < 5 && (int)i.QualityType == acQuality))];
            Item[] consumables = [.. FunGameService.AllItems.Where(i => i.ItemType == ItemType.Consumable && i.IsInGameItem)];
            foreach (Character character in queue.AllCharacters)
            {
                if (addLevel)
                {
                    character.SetLevel(character.Level + 8, false);
                    character.NormalAttack.Level += 1;
                    foreach (Skill skill in character.Skills)
                    {
                        if (skill.SkillType == SkillType.Passive) continue;
                        skill.Level += 1;
                    }
                    Character? original = queue.Original[character.Guid];
                    if (original != null)
                    {
                        original.Level += 8;
                        original.NormalAttack.Level += 1;
                        foreach (Skill skill in original.Skills)
                        {
                            if (skill.SkillType == SkillType.Passive) continue;
                            skill.Level += 1;
                        }
                    }
                }
                Item? mcp = FunGameService.GenerateCoreMagicCardPack(queue.Random, 3, (QualityType)mQuality);
                if (mcp != null)
                {
                    foreach (Skill magic in mcp.Skills.Magics)
                    {
                        magic.Level = character.NormalAttack.Level;
                    }
                    mcp.SetGamingQueue(queue);
                    queue.Equip(character, mcp);
                }
                if (wQuality == -1 && aQuality == -1 && sQuality == -1 && acQuality == -1)
                {
                    continue;
                }
                Item? weapon = null, armor = null, shoe = null, accessory1 = null, accessory2 = null;
                if (FixedWeaponId != 0)
                {
                    // 平衡测试：固定武器，消除「攻击力 20–170（8.5×）+ 副属性各异 + 类型倍率 1.41×」的混淆
                    weapon = FunGameService.Equipment.FirstOrDefault(i => i.Id == FixedWeaponId)
                             ?? FunGameService.AllItems.FirstOrDefault(i => i.Id == FixedWeaponId);
                }
                if (weapon == null && weapons.Length > 0)
                {
                    weapon = weapons[queue.Random.Next(weapons.Length)];
                }
                if (armors.Length > 0)
                {
                    armor = armors[queue.Random.Next(armors.Length)];
                }
                if (shoes.Length > 0)
                {
                    shoe = shoes[queue.Random.Next(shoes.Length)];
                }
                if (accessories.Length > 0)
                {
                    accessory1 = accessories[queue.Random.Next(accessories.Length)];
                }
                if (accessories.Length > 0)
                {
                    accessory2 = accessories[queue.Random.Next(accessories.Length)];
                }
                List<Item> thisDrops = [];
                if (weapon != null) thisDrops.Add(weapon);
                if (armor != null) thisDrops.Add(armor);
                if (shoe != null) thisDrops.Add(shoe);
                if (accessory1 != null) thisDrops.Add(accessory1);
                if (accessory2 != null) thisDrops.Add(accessory2);
                foreach (Item item in thisDrops)
                {
                    Item realItem = item.Copy();
                    realItem.SetGamingQueue(queue);
                    queue.Equip(character, realItem);
                }
                if (consumables.Length > 0 && character.Items.Count < 5)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        Item consumable = consumables[queue.Random.Next(consumables.Length)].Copy();
                        character.Items.Add(consumable);
                    }
                }
            }
        }

        public static void UpdateStatistics(CharacterStatistics totalStats, CharacterStatistics stats)
        {
            // 统计此角色的所有数据
            totalStats.TotalDamage = Calculation.Round2Digits(totalStats.TotalDamage + stats.TotalDamage);
            totalStats.TotalPhysicalDamage = Calculation.Round2Digits(totalStats.TotalPhysicalDamage + stats.TotalPhysicalDamage);
            totalStats.TotalMagicDamage = Calculation.Round2Digits(totalStats.TotalMagicDamage + stats.TotalMagicDamage);
            totalStats.TotalTrueDamage = Calculation.Round2Digits(totalStats.TotalTrueDamage + stats.TotalTrueDamage);
            totalStats.TotalTakenDamage = Calculation.Round2Digits(totalStats.TotalTakenDamage + stats.TotalTakenDamage);
            totalStats.TotalTakenPhysicalDamage = Calculation.Round2Digits(totalStats.TotalTakenPhysicalDamage + stats.TotalTakenPhysicalDamage);
            totalStats.TotalTakenMagicDamage = Calculation.Round2Digits(totalStats.TotalTakenMagicDamage + stats.TotalTakenMagicDamage);
            totalStats.TotalTakenTrueDamage = Calculation.Round2Digits(totalStats.TotalTakenTrueDamage + stats.TotalTakenTrueDamage);
            totalStats.TotalHeal = Calculation.Round2Digits(totalStats.TotalHeal + stats.TotalHeal);
            totalStats.LiveRound += stats.LiveRound;
            totalStats.ActionTurn += stats.ActionTurn;
            totalStats.LiveTime = Calculation.Round2Digits(totalStats.LiveTime + stats.LiveTime);
            totalStats.ControlTime = Calculation.Round2Digits(totalStats.ControlTime + stats.ControlTime);
            totalStats.TotalShield = Calculation.Round2Digits(totalStats.TotalShield + stats.TotalShield);
            totalStats.TotalEarnedMoney += stats.TotalEarnedMoney;
            totalStats.Kills += stats.Kills;
            totalStats.Deaths += stats.Deaths;
            totalStats.Assists += stats.Assists;
            totalStats.FirstKills += stats.FirstKills;
            totalStats.FirstDeaths += stats.FirstDeaths;
            totalStats.LastRank = stats.LastRank;
            double totalRank = totalStats.AvgRank * totalStats.Plays + totalStats.LastRank;
            double totalRating = totalStats.Rating * totalStats.Plays + stats.Rating;
            totalStats.Plays += stats.Plays;
            if (totalStats.Plays != 0) totalStats.AvgRank = Calculation.Round2Digits(totalRank / totalStats.Plays);
            else totalStats.AvgRank = stats.LastRank;
            if (totalStats.Plays != 0) totalStats.Rating = Calculation.Round4Digits(totalRating / totalStats.Plays);
            else totalStats.Rating = stats.Rating;
            totalStats.Wins += stats.Wins;
            totalStats.Top3s += stats.Top3s;
            totalStats.Loses += stats.Loses;
            totalStats.MVPs += stats.MVPs;
            totalStats.UseDecisionPoints += stats.UseDecisionPoints;
            totalStats.TurnDecisions += stats.TurnDecisions;
            if (totalStats.Plays != 0)
            {
                totalStats.AvgDamage = Calculation.Round2Digits(totalStats.TotalDamage / totalStats.Plays);
                totalStats.AvgPhysicalDamage = Calculation.Round2Digits(totalStats.TotalPhysicalDamage / totalStats.Plays);
                totalStats.AvgMagicDamage = Calculation.Round2Digits(totalStats.TotalMagicDamage / totalStats.Plays);
                totalStats.AvgTrueDamage = Calculation.Round2Digits(totalStats.TotalTrueDamage / totalStats.Plays);
                totalStats.AvgTakenDamage = Calculation.Round2Digits(totalStats.TotalTakenDamage / totalStats.Plays);
                totalStats.AvgTakenPhysicalDamage = Calculation.Round2Digits(totalStats.TotalTakenPhysicalDamage / totalStats.Plays);
                totalStats.AvgTakenMagicDamage = Calculation.Round2Digits(totalStats.TotalTakenMagicDamage / totalStats.Plays);
                totalStats.AvgTakenTrueDamage = Calculation.Round2Digits(totalStats.TotalTakenTrueDamage / totalStats.Plays);
                totalStats.AvgHeal = Calculation.Round2Digits(totalStats.TotalHeal / totalStats.Plays);
                totalStats.AvgLiveRound = totalStats.LiveRound / totalStats.Plays;
                totalStats.AvgActionTurn = totalStats.ActionTurn / totalStats.Plays;
                totalStats.AvgLiveTime = Calculation.Round2Digits(totalStats.LiveTime / totalStats.Plays);
                totalStats.AvgControlTime = Calculation.Round2Digits(totalStats.ControlTime / totalStats.Plays);
                totalStats.AvgShield = Calculation.Round2Digits(totalStats.TotalShield / totalStats.Plays);
                totalStats.AvgEarnedMoney = totalStats.TotalEarnedMoney / totalStats.Plays;
                totalStats.Winrate = Calculation.Round4Digits(Convert.ToDouble(totalStats.Wins) / Convert.ToDouble(totalStats.Plays));
                totalStats.Top3rate = Calculation.Round4Digits(Convert.ToDouble(totalStats.Top3s) / Convert.ToDouble(totalStats.Plays));
                totalStats.AvgUseDecisionPoints = totalStats.UseDecisionPoints / totalStats.Plays;
                totalStats.AvgTurnDecisions = totalStats.TurnDecisions / totalStats.Plays;
            }
            if (totalStats.LiveRound != 0) totalStats.DamagePerRound = Calculation.Round2Digits(totalStats.TotalDamage / totalStats.LiveRound);
            if (totalStats.ActionTurn != 0) totalStats.DamagePerTurn = Calculation.Round2Digits(totalStats.TotalDamage / totalStats.ActionTurn);
            if (totalStats.LiveTime != 0) totalStats.DamagePerSecond = Calculation.Round2Digits(totalStats.TotalDamage / totalStats.LiveTime);
        }

        private static void PlaceTeamInCorner(GamingQueue queue, GameMap map, List<Character> teamMembers, HashSet<Grid> allocated, int startX, int endX, int startY, int endY)
        {
            // 确保坐标有效
            startX = Math.Clamp(startX, 0, map.Length - 1);
            endX = Math.Clamp(endX, 0, map.Length - 1);
            startY = Math.Clamp(startY, 0, map.Width - 1);
            endY = Math.Clamp(endY, 0, map.Width - 1);

            // 计算可用空间
            int availableWidth = endX - startX + 1;
            int availableHeight = endY - startY + 1;

            // 计算每行最多角色数（保持至少2格间距）
            int maxPerRow = Math.Min(availableWidth, Math.Max(1, availableWidth / 3));

            // 计算起始位置
            int currentX = startX;
            int currentY = endY; // 从底部开始放置

            foreach (Character character in teamMembers)
            {
                character.NormalAttack.GamingQueue = queue;
                Grid? grid = null;
                int attempts = 0;

                // 尝试在指定区域找到合适位置
                do
                {
                    // 获取网格
                    grid = map[currentX, currentY, 0];

                    // 如果网格无效或已被占用，尝试下一个位置
                    if (grid == null || allocated.Contains(grid))
                    {
                        // 移动到下一个位置
                        currentX++;

                        // 换行处理
                        if (currentX > endX || currentX - startX >= maxPerRow)
                        {
                            currentX = startX;
                            currentY--; // 向上移动一行

                            // 如果超出区域，回到底部
                            if (currentY < startY)
                            {
                                currentY = endY;
                            }
                        }
                    }

                    attempts++;

                    // 如果尝试次数过多，使用随机位置
                    if (attempts > teamMembers.Count * 2)
                    {
                        grid = GetRandomGridInArea(queue.Random, map, allocated, startX, endX, startY, endY);
                        break;
                    }
                }
                while (grid == null || allocated.Contains(grid));

                // 放置角色
                if (grid != null)
                {
                    allocated.Add(grid);
                    map.SetCharacterCurrentGrid(character, grid);

                    // 移动到下一个位置（保持间距）
                    currentX += 2; // 水平间距2格

                    // 换行处理
                    if (currentX > endX || currentX - startX >= maxPerRow)
                    {
                        currentX = startX;
                        currentY -= 2; // 垂直间距2格

                        // 如果超出区域，回到底部
                        if (currentY < startY)
                        {
                            currentY = endY;
                        }
                    }
                }
                else
                {
                    // 回退到随机放置
                    Grid randomGrid = map.Grids.Values.FirstOrDefault(g => !allocated.Contains(g)) ?? Grid.Empty;
                    allocated.Add(randomGrid);
                    map.SetCharacterCurrentGrid(character, randomGrid);
                }
            }
        }

        private static Grid? GetRandomGridInArea(Random random, GameMap map, HashSet<Grid> allocated, int startX, int endX, int startY, int endY)
        {
            List<Grid> availableGrids = [];

            for (int x = startX; x <= endX; x++)
            {
                for (int y = startY; y <= endY; y++)
                {
                    Grid? grid = map[x, y, 0];
                    if (grid != null && !allocated.Contains(grid))
                    {
                        availableGrids.Add(grid);
                    }
                }
            }

            return availableGrids.Count > 0 ?
                availableGrids[random.Next(availableGrids.Count)] :
                null;
        }

        private static string DisplayMapInConsole(GameMap map)
        {
            StringBuilder sb = new();

            // 打印列坐标
            sb.Append("   ");
            for (int x = 0; x < map.Length; x++)
            {
                sb.Append($" {x}  ");
            }
            sb.AppendLine();

            for (int y = 0; y < map.Width; y++)
            {
                // 打印行坐标
                sb.Append($"{y}  ");

                for (int x = 0; x < map.Length; x++)
                {
                    Grid? grid = map[x, y, 0];

                    if (grid is null)
                    {
                        sb.Append("    ");
                        continue;
                    }

                    // 检查格子上是否有角色
                    if (grid.Characters.Count > 0)
                    {
                        // 取第一个角色的昵称首字母
                        Character character = grid.Characters.First();
                        string displayChar = character.NickName.Length > 0 ? character.NickName[0].ToString().ToUpper() : "?";
                        sb.Append($"[{displayChar}] ");
                    }
                    else
                    {
                        sb.Append(" .  ");
                    }
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }

    public class FastAutoMap : GameMap
    {
        public override string Name => OshimaGameModuleConstant.FastAutoMap;
        public override string Description => OshimaGameModuleConstant.Description;
        public override string Version => OshimaGameModuleConstant.Version;
        public override string Author => OshimaGameModuleConstant.Author;

        public override int Length => 9;
        public override int Width => 9;
        public override int Height => 6;

        public override float Size => 12;

        public override GameMap InitGamingQueue(IGamingQueue queue)
        {
            GameMap map = new FastAutoMap();
            map.Load();

            if (queue is GamingQueue gq)
            {
                gq.WriteLine($"地图 {map.Name} 已加载。");
            }

            return map;
        }
    }
}