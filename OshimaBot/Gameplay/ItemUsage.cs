using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;

namespace OshimaBot.Gameplay;

/// <summary>
/// 用 Core 的 <see cref="User"/> 承载一次「库存使用物品」。
/// <para>物品的行为本来就定义在内容包里（经验书 <c>OnItemUsed</c> 走 <c>GetEXP</c> 效果、
/// 回复药回血、礼包开包），所以这里只负责装配一个临时 <see cref="User"/>、调用
/// <see cref="Item.UseItem(User, int, Dictionary{string, object})"/>，再把结果差值交回调用方。</para>
/// <para>不再使用自造的「经验 = 30 × 品质」公式——旧版也没有这种统一公式。</para>
/// </summary>
public static class ItemUsage
{
    /// <summary>旧版 <c>FunGameConstant.ItemCanUsed</c>：只有这几类物品允许在库存里使用。</summary>
    public static bool CanUse(Item item) => item.ItemType
        is ItemType.Consumable
        or ItemType.MagicCard
        or ItemType.SpecialItem
        or ItemType.GiftBox
        or ItemType.Others;

    /// <param name="ExpGained">每个角色实际获得的经验（按传入的 <see cref="Character"/> 引用对应）。</param>
    /// <param name="CreditDelta">物品自身给出的金币变动（如礼包），调用方需走 ledger 记账。</param>
    /// <param name="MaterialDelta">物品自身给出的材料变动。</param>
    public sealed record Result(
        bool Used, string Message,
        IReadOnlyDictionary<Character, double> ExpGained,
        double CreditDelta, double MaterialDelta);

    public static Result Use(PlayerAccount account, PlayerItem item, int times, IReadOnlyList<PlayerCharacter> targets)
    {
        // 临时 User：只作为物品行为的载体，货币是副本，改动不影响真实钱包。
        User user = new(account.UserId, $"旅人{account.UserId}");
        user.Inventory.Credits = account.Credits;
        user.Inventory.Materials = account.Materials;
        foreach (PlayerCharacter target in targets) user.Inventory.Characters.Add(target.Core);

        Item core = item.Item;
        core.User = user;                       // UseItem 要求 Item.User 非空才执行

        Dictionary<Character, double> before = [];
        foreach (PlayerCharacter target in targets) before[target.Core] = target.Core.EXP;

        Dictionary<string, object> args = new()
        {
            ["targets"] = targets.Select(x => x.Core).ToArray(),
            ["useCount"] = times
        };

        bool used = core.UseItem(user, times, args);
        string message = args.TryGetValue("msg", out object? raw) && raw is string text ? text : "";

        Dictionary<Character, double> gained = [];
        foreach ((Character character, double exp) in before)
        {
            double delta = character.EXP - exp;
            if (delta != 0) gained[character] = delta;
        }

        return new Result(
            used, message, gained,
            user.Inventory.Credits - account.Credits,
            user.Inventory.Materials - account.Materials);
    }
}
