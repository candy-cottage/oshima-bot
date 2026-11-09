using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;

namespace OshimaBot.Content.Items
{
    public class 混沌之核() : Item(ItemType.SpecialItem)
    {
        public override long Id => (long)SpecialItemID.混沌之核;
        public override string Name => "混沌之核";
        public override string Description => "升级技能必备的特级材料。";
        public override QualityType QualityType => QualityType.Purple;
    }
}
