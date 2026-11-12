using FunGame.Core.Entity;

namespace OshimaBot.Content.Regions;

/// <summary>
/// 地区注册表（旧版由 <c>FunGameService.Regions</c> 装载 <c>OshimaModules/Regions/*</c>）。
/// <para>构造顺序按地区 Id 排好，<c>/查地区</c> 的编号与旧版一致（0 = 铎京城）。</para>
/// </summary>
public static class OshimaRegions
{
    /// <summary>新建一份地区实例（每次调用都是新对象，天气会重新随机）。</summary>
    public static List<OshimaRegion> CreateAll() =>
    [
        new 铎京城(),        // 0  此地
        new 银辉城(),        // 1  奇异
        new 瑟兰薇歌林海(),  // 2  生态
        new 永燃坩埚(),      // 3  矿区
        new 永霜裂痕(),      // 4  奇异
        new 齿轮坟场(),      // 5  机械
        new 千瞳镜湖(),      // 6  奇异
        new 时之荒漠(),      // 7  奇异
        new 谵妄海市(),      // 8  奇异
        new 腐萤沼渊(),      // 9  生态
        new 双生月崖(),      // 10 生态
        new 棱镜骨桥(),      // 11 生态
        new 泰坦遗迹()       // 12 世外
    ];

    /// <summary>地区分类（旧版 <c>Region.Category</c>）。</summary>
    public static readonly IReadOnlyList<string> Categories = ["此地", "奇异", "生态", "矿区", "机械", "世外"];

    public static OshimaRegion? Find(IEnumerable<OshimaRegion> regions, long id)
        => regions.FirstOrDefault(r => r.Id == id);

    public static OshimaRegion? FindByName(IEnumerable<OshimaRegion> regions, string name)
        => regions.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.Ordinal));
}
