using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Regions;
using OshimaBot.Content.Units;
using OshimaBot.Game.Services;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// 地区内容（从旧版 <c>OshimaModules/Regions/*</c> 复制而来）。
/// <para>这些是**数据**，不是逻辑：测试主要防「复制时漏了地区 / 漏了敌人与材料」，
/// 顺便钉住 Id 顺序（/查地区 的编号依赖它）。</para>
/// </summary>
public sealed class RegionContentTests
{
    [Fact]
    public void Regions_AreRegisteredInIdOrder()
    {
        FunGameRuntime.EnsureInitialized();

        List<Region> regions = FunGameService.Regions;
        Assert.Equal(13, regions.Count);
        Assert.Equal(Enumerable.Range(0, 13).Select(x => (long)x), regions.Select(r => r.Id));
        Assert.Equal("铎京城", regions[0].Name);
        Assert.Equal("泰坦遗迹", regions[^1].Name);
        Assert.All(regions, region => Assert.False(string.IsNullOrWhiteSpace(region.Name)));
        Assert.All(regions, region => Assert.False(string.IsNullOrWhiteSpace(region.Category)));
    }

    [Fact]
    public void Regions_AllHaveUsableWeather()
    {
        FunGameRuntime.EnsureInitialized();

        foreach (Region region in FunGameService.Regions)
        {
            Assert.NotEmpty(region.Weathers);
            // ChangeRandomWeather 已经把 Weather 设成某个键；温度必须能对上
            Assert.True(region.Weathers.ContainsKey(region.Weather), $"{region.Name} 的天气不在表里");
            Assert.Equal(region.Weathers[region.Weather], region.Temperature);
        }
    }

    [Fact]
    public void Regions_KeepEnemiesAndMaterialsFromLegacy()
    {
        FunGameRuntime.EnsureInitialized();
        OshimaRegion silver = OshimaRegions.Find(OshimaRegions.CreateAll(), 1)!;

        Assert.Equal("银辉城", silver.Name);
        Assert.Equal("奇异", silver.Category);
        Assert.Equal(RarityType.FiveStar, silver.Difficulty);
        Assert.Equal(["失控的悖论引擎", "星辉巨母"], silver.BossNames);
        Assert.Equal(["星银守卫", "星辉水母"], silver.UnitNames);
        Assert.Equal(["星银合金", "液态月光", "星辉凝露"], silver.RegionCrops.Select(c => c.Name));
        Assert.Equal(6, silver.AllQuests.Count());
        Assert.Equal(5, silver.Areas.Count);
    }

    [Fact]
    public void Regions_KeepWeatherPredicates()
    {
        FunGameRuntime.EnsureInitialized();
        OshimaRegion cliff = OshimaRegions.FindByName(OshimaRegions.CreateAll(), "双生月崖")!;

        // 光鹰只在永昼出现、浮空岛灵只在永夜出现
        Assert.Equal(["光鹰", "浮空岛灵"], cliff.UnitNames);
        RegionUnit eagle = cliff.RegionUnits.First(u => u.Name == "光鹰");
        Assert.NotEmpty(eagle.GenerationPredicates);

        cliff.ChangeWeather("永昼");
        Assert.All(eagle.GenerationPredicates, predicate => Assert.True(predicate(cliff)));

        cliff.ChangeWeather("永夜");
        Assert.All(eagle.GenerationPredicates, predicate => Assert.False(predicate(cliff)));

        // 影玫瑰只在永夜
        var rose = cliff.RegionCrops.First(c => c.Name == "影玫瑰");
        Assert.All(rose.GenerationPredicates, predicate => Assert.True(predicate(cliff)));
    }

    [Fact]
    public void Regions_MainCityHasNoCombatContent()
    {
        FunGameRuntime.EnsureInitialized();
        OshimaRegion city = OshimaRegions.Find(OshimaRegions.CreateAll(), 0)!;

        Assert.Equal("铎京城", city.Name);
        Assert.Equal("此地", city.Category);
        Assert.Empty(city.Characters);
        Assert.Empty(city.Units);
        Assert.Empty(city.Crops);
        Assert.Empty(city.AllQuests);      // 商店逻辑没有跟过来（v3 用 StoreCatalog）
    }

    [Fact]
    public void Regions_ProgressiveQuestsNameTheirMaterial()
    {
        FunGameRuntime.EnsureInitialized();

        foreach (OshimaRegion region in OshimaRegions.CreateAll())
        {
            foreach ((string key, QuestExploration quest) in region.ProgressiveQuestList)
            {
                Assert.False(string.IsNullOrWhiteSpace(quest.Item), $"{region.Name} / {key} 缺少材料名");
                Assert.Contains("{0}", quest.Description);
            }
            Assert.All(region.AllQuests, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value.Description)));
        }
    }
}
