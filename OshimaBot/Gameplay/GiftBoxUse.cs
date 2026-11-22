using System.Text.Json;
using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Items;

namespace OshimaBot.Gameplay;

/// <summary>
/// 礼包使用（旧版 <c>FunGameService.UseItemCustom</c>）。
/// <para>为什么放在玩法层而不是内容包：Core 的 <c>ItemConverter.NewInstance()</c> 固定还原成基类
/// <see cref="Item"/>，存档里的礼包取出来不是 <c>年夜饭</c>/<c>魔法卡礼包</c> 这些子类，
/// <c>item is GiftBox</c> 永远为假、<c>OnItemUsed</c> 也是基类空实现 —— 这就是礼包使用失败的根因。
/// 这里改为按 <c>CatalogId</c> 回内容包找**模板**（模板是完整具体类，带 Gifts），把奖励解析出来交调用方落库。</para>
/// </summary>
public static class GiftBoxUse
{
    /// <summary>一条礼包奖励：金币 / 钻石 / 具体物品名 / 魔法卡礼包（品质+张数）。</summary>
    public sealed record Reward(string Kind, string Name, int Count, QualityType Quality = QualityType.White);

    /// <summary>
    /// 用 Factory 按 CatalogId 构造模板（与原版 <c>Factory.GetInstance</c> 同路径），读出礼包内容。
    /// <para><paramref name="saved"/> 是存档里还原出的基类 Item：魔法卡礼包的品质/张数存在它的 Others 里，
    /// Factory 构造出的默认模板是白品×1，必须用存档值覆盖 —— 否则橙品×5 会被开成白品×1。</para>
    /// </summary>
    public static IReadOnlyList<Reward>? ReadRewards(string catalogId, Item? saved = null)
    {
        if (!long.TryParse(catalogId, out long id)) return null;
        Item template = Factory.OpenFactory.GetInstance<Item>(id, saved?.Name ?? "", []);
        if (template.ItemType != ItemType.GiftBox) return null;

        if (template is 魔法卡礼包 cardBox)
        {
            int count = cardBox.Count;
            QualityType quality = cardBox.QualityType;
            if (saved is not null)
            {
                if (ReadInt(saved, "Count") is int savedCount) count = savedCount;
                if (ReadInt(saved, "QualityType") is int savedQuality) quality = (QualityType)savedQuality;
            }
            return [new Reward("magic-card", $"魔法卡（{ItemSet.GetQualityTypeName(quality)}）", count, quality)];
        }
        return ReadRewards(template);
    }

    /// <summary>从 Others 读整数：兼容 int / long / JsonElement（反序列化后的形态）。</summary>
    private static int? ReadInt(Item item, string key)
    {
        if (!item.Others.TryGetValue(key, out object? value)) return null;
        return value switch
        {
            int i => i,
            long l => (int)l,
            JsonElement e when e.TryGetInt32(out int v) => v,
            _ => int.TryParse(value.ToString(), out int n) ? n : null
        };
    }

    /// <summary>从模板对象读礼包内容（测试可直接传具体礼包类）。</summary>
    public static IReadOnlyList<Reward>? ReadRewards(Item? template)
    {
        if (template is null || template.ItemType != ItemType.GiftBox) return null;

        // 魔法卡礼包：生成同品质、随机属性与技能的魔法卡（旧版 UseItemCustom 第一支）。
        if (template is 魔法卡礼包 cardBox)
        {
            return [new Reward("magic-card", $"魔法卡（{ItemSet.GetQualityTypeName(cardBox.QualityType)}）", cardBox.Count, cardBox.QualityType)];
        }

        if (template is not 礼包.GiftBox box || box.Gifts.Count == 0) return null;

        string currency = General.GameplayEquilibriumConstant.InGameCurrency;
        string material = General.GameplayEquilibriumConstant.InGameMaterial;
        List<Reward> rewards = [];
        foreach ((string name, int count) in box.Gifts)
        {
            if (name == currency) rewards.Add(new Reward("credits", currency, count));
            else if (name == material) rewards.Add(new Reward("diamonds", material, count));
            else if (TryParseCardPack(name, out QualityType quality))
                rewards.Add(new Reward("magic-card-pack", name, count, quality));
            else
                rewards.Add(new Reward("item", name, count));
        }
        return rewards;
    }

    /// <summary>「普通魔法卡礼包」等名字 → 品质，照抄旧版 UseItemCustom 的字典。</summary>
    private static bool TryParseCardPack(string name, out QualityType quality)
    {
        if (!name.Contains("魔法卡礼包", StringComparison.Ordinal))
        {
            quality = default;
            return false;
        }
        foreach ((string prefix, QualityType type) in new (string, QualityType)[]
        {
            ("普通", QualityType.White), ("优秀", QualityType.Green), ("稀有", QualityType.Blue),
            ("史诗", QualityType.Purple), ("传说", QualityType.Orange), ("神话", QualityType.Red),
            ("不朽", QualityType.Gold)
        })
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                quality = type;
                return true;
            }
        }
        quality = default;
        return false;
    }

    /// <summary>奖励的可读摘要，用于回复正文（旧版「打开礼包成功！获得了以下物品：…」）。</summary>
    public static string Describe(IReadOnlyList<Reward> rewards)
        => string.Join("，", rewards.Select(x => x.Kind switch
        {
            "credits" or "diamonds" => $"{x.Name} ×{x.Count}",
            _ => $"{x.Name} ×{x.Count}"
        }));
}
