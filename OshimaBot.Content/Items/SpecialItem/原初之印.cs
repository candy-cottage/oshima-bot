using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;

namespace OshimaBot.Content.Items
{
    public class 原初之印() : Item(ItemType.SpecialItem)
    {
        public override long Id => (long)SpecialItemID.原初之印;
        public override string Name => "原初之印";
        public override string Description => "角色突破等阶必备的特级材料。";
        public override QualityType QualityType => QualityType.Purple;
    }
}
