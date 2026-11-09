using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;

namespace OshimaBot.Content.Effects.OpenEffects
{
    public class ExINT2 : Effect
    {
        public override long Id => (long)EffectID.ExINT2;
        public override string Name { get; set; } = "智力加成";
        public override string Description => $"{(实际加成 >= 0 ? "增加" : "减少")}角色 {Math.Abs(加成比例) * 100:0.##}% [ {(实际加成 == 0 ? "基于基础智力" : $"{Math.Abs(实际加成):0.##}")} ] 点智力。" + (Source != null && (Skill.Character != Source || Skill is not OpenSkill) ? $"来自：[ {Source} ]" + (Skill.Item != null ? $" 的 [ {Skill.Item.Name} ]" : (Skill is OpenSkill ? "" : $" 的 [ {Skill.Name} ]")) : "");
        public double Value => 实际加成;

        private readonly double 加成比例 = 0;
        private double 实际加成 = 0;
        private bool 加成已应用 = false;
        private double 已应用加成比例 = 0;

        public override void OnEffectGained(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (!加成已应用)
            {
                // 仅首次获得时初始化剩余时间，避免后续刷新时被重置
                if (Durative && RemainDuration == 0)
                {
                    RemainDuration = Duration;
                }
                else if (RemainDurationTurn == 0)
                {
                    RemainDurationTurn = DurationTurn;
                }
            }
            else
            {
                // 已生效过：先还原旧值再套用新值，避免重复叠加
                character.ExINTPercentage -= 已应用加成比例;
            }
            实际加成 = character.BaseINT * 加成比例;
            已应用加成比例 = 加成比例;
            character.ExINTPercentage += 已应用加成比例;
            加成已应用 = true;
        }

        public override void OnEffectLost(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (!加成已应用) return;
            character.ExINTPercentage -= 已应用加成比例;
            已应用加成比例 = 0;
            实际加成 = 0;
            加成已应用 = false;
        }

        public override void OnAttributeChanged(HookContext ctx)
        {
            // 属性侧是百分比加成，基础智力变化时会实时反映到 ExINT3，无需重新结算；
            // 此处只更新描述中展示的点数
            if (ctx.Trigger is Character character && 加成已应用)
            {
                实际加成 = character.BaseINT * 加成比例;
            }
        }

        public ExINT2(Skill skill, Dictionary<string, object> args, Character? source = null) : base(skill, args)
        {
            EffectType = EffectType.Item;
            GamingQueue = skill.GamingQueue;
            Source = source;
            if (Values.Count > 0)
            {
                string key = Values.Keys.FirstOrDefault(s => s.Equals("exint", StringComparison.CurrentCultureIgnoreCase)) ?? "";
                if (key.Length > 0 && double.TryParse(Values[key].ToString(), out double exINT))
                {
                    加成比例 = exINT;
                }
            }
        }
    }
}
