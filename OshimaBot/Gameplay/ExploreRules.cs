using FunGame.Core.Library.Constant;
using OshimaBot.Content.Regions;

namespace OshimaBot.Gameplay;

/// <summary>
/// 探索规则（旧版 <c>FunGameConstant.MaxExploreTimes / ExploreTime / ExploreResultProbabilities</c>
/// 与 <c>FunGameService.GenerateExploreModel</c> 的数值）。
/// </summary>
public static class ExploreRules
{
    /// <summary>每日基础探索许可（旧版 <c>MaxExploreTimes = 60</c>）。</summary>
    public const int DailyQuota = 60;

    /// <summary>午餐 / 晚餐各加 30（11:00–13:59、17:00–19:59 首次上线）。</summary>
    public const int MealBonus = 30;

    /// <summary>探索时长：2 分钟（旧版 <c>ExploreTime = 2</c>）。</summary>
    public const int DurationMinutes = 2;

    /// <summary>一次探索最多带几个角色（旧版指令层限制 4 个）。</summary>
    public const int MaxCharacters = 4;

    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(DurationMinutes);

    /// <summary>旧版 <c>ExploreResultProbabilities</c>：普通 45% / 无事 20% / 战斗 25% / 拾得 10% / 事件 0%。</summary>
    public static readonly IReadOnlyDictionary<ExploreResult, double> BaseProbabilities = new Dictionary<ExploreResult, double>
    {
        [ExploreResult.General] = 0.45,
        [ExploreResult.Nothing] = 0.20,
        [ExploreResult.Fight] = 0.25,
        [ExploreResult.Earned] = 0.10,
        [ExploreResult.Event] = 0.0
    };

    /// <summary>难度星级（1–5），探索许可按「角色数 × 星级」扣。</summary>
    public static int Stars(RarityType difficulty) => difficulty switch
    {
        RarityType.OneStar => 1,
        RarityType.TwoStar => 2,
        RarityType.ThreeStar => 3,
        RarityType.FourStar => 4,
        _ => 5
    };

    /// <summary>按难度调整后的结果概率（旧版 <c>GenerateExploreModel</c> 的 switch）。</summary>
    public static Dictionary<ExploreResult, double> Probabilities(int stars)
    {
        Dictionary<ExploreResult, double> probabilities = new(BaseProbabilities);
        switch (stars)
        {
            case 2:
                probabilities[ExploreResult.General] -= 0.10;
                probabilities[ExploreResult.Fight] += 0.10;
                break;
            case 3:
                probabilities[ExploreResult.General] -= 0.15;
                probabilities[ExploreResult.Earned] -= 0.05;
                probabilities[ExploreResult.Nothing] += 0.05;
                probabilities[ExploreResult.Fight] += 0.15;
                break;
            case 4:
                probabilities[ExploreResult.General] -= 0.30;
                probabilities[ExploreResult.Earned] -= 0.05;
                probabilities[ExploreResult.Fight] += 0.35;
                break;
            case 5:
                probabilities[ExploreResult.General] -= 0.35;
                probabilities[ExploreResult.Earned] -= 0.05;
                probabilities[ExploreResult.Nothing] -= 0.05;
                probabilities[ExploreResult.Fight] += 0.45;
                break;
        }
        return probabilities;
    }

    public static ExploreResult Roll(IRandomSource random, int stars)
    {
        double roll = random.NextDouble();
        double cumulative = 0;
        foreach ((ExploreResult result, double probability) in Probabilities(stars))
        {
            cumulative += probability;
            if (roll <= cumulative) return result;
        }
        return ExploreResult.Nothing;
    }

    /// <summary>遇敌数量：1–2 星 1 个、3–4 星 2 个、5 星 3 个（旧版同）。</summary>
    public static int EnemyCount(int stars) => stars switch
    {
        <= 2 => 1,
        <= 4 => 2,
        _ => 3
    };

    public static string ResultName(ExploreResult result) => result switch
    {
        ExploreResult.General => "平淡",
        ExploreResult.Nothing => "空手",
        ExploreResult.Fight => "遭遇战",
        ExploreResult.Earned => "拾得",
        _ => "事件"
    };

    /// <summary>普通结果的金币：300 × 星级 × 人数。</summary>
    public static long Credits(int stars, int characters) => 300L * Math.Max(1, stars) * Math.Max(1, characters);

    /// <summary>拾得材料的件数：星级 × 人数的一半（向上取整）。</summary>
    public static int Materials(int stars, int characters)
        => (int)Math.Ceiling(Math.Max(1, stars) * Math.Max(1, characters) / 2.0);

    /// <summary>战斗胜利的材料件数（比拾得多一半）。</summary>
    public static int BattleMaterials(int stars, int characters)
        => (int)Math.Ceiling(Math.Max(1, stars) * Math.Max(1, characters) * 0.75);

    /// <summary>探索许可消耗：角色数 × 星级。</summary>
    public static int Cost(int characters, int stars) => Math.Max(1, characters) * Math.Max(1, stars);
}
