using FunGame.Core.Library.Constant;
using OshimaBot.Content.Regions;
using OshimaBot.Game.Services;

namespace OshimaBot.Gameplay;

/// <summary>配方里的一行：某种材料来自哪个地区、多少件。</summary>
public sealed record ForgeMaterial(string Name, int Count, long RegionId, string RegionName, RarityType Difficulty);

/// <summary>
/// 一次锻造的推演结果（纯函数算出来，模拟锻造与真实锻造共用）。
/// </summary>
public sealed record ForgePlan(
    bool Ok, string Message,
    int EffectiveCount, QualityType Quality,
    long RegionId, string RegionName,
    IReadOnlyDictionary<long, double> RegionShares,
    double GeneralPoints, double SuccessPoints, double FailPoints, bool Success);

/// <summary>
/// 锻造规则：照搬旧版 <c>FunGameConstant.ForgeNeedy / ForgeRegionCoefficient</c>
/// 与 <c>FunGameService</c> 里那段「按材料所属地区折算」的算法。
/// </summary>
public static class ForgeRules
{
    /// <summary>成本：每次锻造 200 金币（旧版 <c>ForgeCost</c>）。</summary>
    public const long CostCredits = 200;

    /// <summary>默认时长 5 分钟，上限 120 分钟。</summary>
    public const int DefaultMinutes = 5;
    public const int MaxMinutes = 120;

    /// <summary>品质门槛（旧版 <c>ForgeNeedy</c>）：达到多少有效用量才能出这一档。</summary>
    public static readonly IReadOnlyDictionary<QualityType, double> Needy = new Dictionary<QualityType, double>
    {
        [QualityType.Red] = 230,
        [QualityType.Orange] = 170,
        [QualityType.Purple] = 120,
        [QualityType.Blue] = 80,
        [QualityType.Green] = 50,
        [QualityType.White] = 30
    };

    /// <summary>地区系数（旧版 <c>ForgeRegionCoefficient</c>）：星级越高，材料越"值钱"。</summary>
    public static readonly IReadOnlyDictionary<RarityType, double> Coefficients = new Dictionary<RarityType, double>
    {
        [RarityType.OneStar] = 0.5,
        [RarityType.TwoStar] = 0.6,
        [RarityType.ThreeStar] = 0.7,
        [RarityType.FourStar] = 0.85,
        [RarityType.FiveStar] = 1.0
    };

    public const double GeneralPointsPerCount = 0.3;
    public const double FailPointsPerCount = 0.2;

    /// <summary>有效用量下限（低于这个数不能开始锻造，同旧版 <c>ForgeNeedy[White]</c>）。</summary>
    public static double MinCount => Needy[QualityType.White];

    public static double Coefficient(RarityType difficulty)
        => Coefficients.TryGetValue(difficulty, out double value) ? value : 1.0;

    public static QualityType QualityOf(double count)
    {
        if (count >= Needy[QualityType.Red]) return QualityType.Red;
        if (count >= Needy[QualityType.Orange]) return QualityType.Orange;
        if (count >= Needy[QualityType.Purple]) return QualityType.Purple;
        if (count >= Needy[QualityType.Blue]) return QualityType.Blue;
        if (count >= Needy[QualityType.Green]) return QualityType.Green;
        return QualityType.White;
    }

    /// <summary>
    /// 推演结果。<paramref name="roll"/> 只用于「多地区」时按贡献占比抽产地。
    /// <para>旧版有个反直觉的口径：**单一地区不乘系数**（count 就是材料件数），
    /// 混合地区才按 <c>数量 × 系数</c> 折算 —— 这里保持不变，并在结果里说明。</para>
    /// </summary>
    public static ForgePlan Plan(IReadOnlyList<ForgeMaterial> materials, double roll)
    {
        if (materials.Count == 0)
        {
            return Failed("配方是空的，先提交材料");
        }

        Dictionary<long, (string Name, double Count, RarityType Difficulty)> regions = [];
        foreach (ForgeMaterial material in materials)
        {
            if (material.RegionId <= 0) continue;      // 主城材料不参与锻造（旧版 region.Id == 0 跳过）
            double equivalent = material.Count * Coefficient(material.Difficulty);
            if (regions.TryGetValue(material.RegionId, out var existing))
            {
                regions[material.RegionId] = (existing.Name, existing.Count + equivalent, existing.Difficulty);
            }
            else
            {
                regions[material.RegionId] = (material.RegionName, equivalent, material.Difficulty);
            }
        }
        if (regions.Count == 0)
        {
            return Failed("提交的材料都不属于任何野外地区（主城材料不能锻造）");
        }

        bool single = regions.Count == 1;
        (long regionId, string regionName, double weight, RarityType difficulty) result;
        double count;
        if (single)
        {
            (long id, var value) = regions.First();
            count = materials.Sum(x => (double)x.Count);
            result = (id, value.Name, count, value.Difficulty);
        }
        else
        {
            count = Math.Floor(regions.Values.Sum(x => x.Count));
            double cumulative = 0;
            result = (regions.First().Key, regions.First().Value.Name, count, regions.First().Value.Difficulty);
            foreach ((long id, var value) in regions)
            {
                cumulative += value.Count / count;
                if (roll <= cumulative)
                {
                    result = (id, value.Name, count, value.Difficulty);
                    break;
                }
            }
        }

        if (count < MinCount)
        {
            return Failed($"有效材料用量 {count:0.##} 不足最低要求 {MinCount:0}，再凑点材料");
        }

        QualityType quality = QualityOf(count);
        Dictionary<long, double> shares = regions.ToDictionary(
            pair => pair.Key,
            pair => single ? 1.0 : pair.Value.Count / count);

        return new ForgePlan(true,
            single
                ? $"单一产地【{result.regionName}】，按材料件数 {count:0.##} 计"
                : $"混合产地 {regions.Count} 处，按「数量 × 地区系数」折算为 {count:0.##}",
            (int)count, quality, result.regionId, result.regionName, shares,
            count * GeneralPointsPerCount,
            count - Needy[quality],
            count * FailPointsPerCount,
            Success: true);
    }

    /// <summary>
    /// 产物：优先该地区同品质装备；没有就退化成该地区的同品质材料；
    /// 都没有则锻造失败（旧版同样会因为「该地区不存在该品质的物品」而失败）。
    /// <para>v3 的装备没有产地标记，所以装备池是内容包全量（结果里会注明产地只影响系数与品质）。</para>
    /// </summary>
    public static FunGame.Core.Entity.Item? Product(long regionId, QualityType quality, Random random)
    {
        List<FunGame.Core.Entity.Item> equipment = [.. FunGameService.Equipment
            .Where(item => item.QualityType == quality)];
        if (equipment.Count > 0) return equipment[random.Next(equipment.Count)].Copy();

        OshimaRegion? region = FunGameService.Regions.OfType<OshimaRegion>().FirstOrDefault(r => r.Id == regionId);
        List<Content.Items.RegionItem> crops = region is null
            ? []
            : [.. region.RegionCrops.Where(c => c.QualityType == quality)];
        return crops.Count == 0 ? null : crops[random.Next(crops.Count)].Copy();
    }

    public static string QualityName(QualityType quality) => ItemSet.GetQualityTypeName(quality);

    private static ForgePlan Failed(string message)
        => new(false, message, 0, QualityType.White, 0, "", new Dictionary<long, double>(), 0, 0, 0, false);
}
