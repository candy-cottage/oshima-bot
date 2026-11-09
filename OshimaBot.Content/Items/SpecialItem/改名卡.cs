using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;

namespace OshimaBot.Content.Items
{
    public class 改名卡() : Item(ItemType.SpecialItem)
    {
        public override long Id => (long)SpecialItemID.改名卡;
        public override string Name => "改名卡";
        public override string Description => "拥有改名卡时，可以使用【自定义改名】指令，来自定义个性化昵称。新昵称需要经过审核。";
        public override QualityType QualityType => QualityType.Orange;
    }
}
