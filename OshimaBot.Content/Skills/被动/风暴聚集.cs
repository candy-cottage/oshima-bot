using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;

namespace OshimaBot.Content.Skills
{
    public class 风暴聚集 : Skill
    {
        public override long Id => (long)PassiveID.风暴聚集;
        public override string Name => "风暴聚集";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        /// <summary>
        /// 已累计提升的核心属性值。[ 永久成长数据存放在技能本体上 ]：死亡/复活时角色会被重建，
        /// 依赖 <see cref="OnCharacterRespawn(Skill)"/> 把累计值迁移给新技能实例，特效重挂后全量施加。
        /// </summary>
        public double 累计提升 { get; set; } = 0;

        public 风暴聚集(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 风暴聚集特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }

        public override void OnCharacterRespawn(Skill newSkill)
        {
            if (newSkill is 风暴聚集 s)
            {
                s.累计提升 = 累计提升;
            }
        }
    }

    public class 风暴聚集特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"每经过 {间隔时间:0.##} {GameplayEquilibriumConstant.InGameTime}，永久提升 {每次提升:0.##} 点{CharacterSet.GetPrimaryAttributeName(Skill.Character?.PrimaryAttribute ?? PrimaryAttribute.STR)}（核心属性），无上限。" +
            (CSkill?.累计提升 > 0 ? $"（当前已累计提升 {CSkill.累计提升:0.##} 点核心属性）" : "");

        public 风暴聚集? CSkill => Skill as 风暴聚集;

        private double 已应用 = 0;
        private double 距离下次提升 = 0;

        private double 间隔时间 => Skill.Character != null ? 60 - Skill.Character.Level * 0.15 : 60;
        private double 每次提升 => Skill.Character != null ? 1 + Skill.Character.Level * 0.08 : 1;

        public override void OnEffectGained(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (Skill.Character == null || Skill.Character != character) return;
            距离下次提升 = 间隔时间;
            // 复活/重挂时：把技能本体上继承到的累计值整体施加一次
            刷新(character);
        }

        public override void OnTimeElapsed(TimeLapseContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (Skill.Character == null || Skill.Character != character) return;
            距离下次提升 -= ctx.Elapsed;
            if (距离下次提升 <= 0)
            {
                距离下次提升 += 间隔时间;
                if (CSkill != null) CSkill.累计提升 += 每次提升;
                刷新(character);
                WriteLine($"[ {character} ] 发动了风暴聚集！核心属性永久提升了 {每次提升:0.##} 点（当前共 {CSkill?.累计提升:0.##} 点）。");
            }
            // 自愈式幂等刷新：复活等场景下若 OnEffectGained 未重放，首个时间流逝即补挂全量累计
            刷新(character);
        }

        public override void OnEffectLost(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (已应用 <= 0) return;
            // 只回收本实例已施加的加成；累计值保留在技能本体上，交由复活迁移/重挂后重新施加
            switch (character.PrimaryAttribute)
            {
                case PrimaryAttribute.AGI:
                    character.ExAGI2 -= 已应用;
                    break;
                case PrimaryAttribute.INT:
                    character.ExINT2 -= 已应用;
                    break;
                default:
                    character.ExSTR2 -= 已应用;
                    break;
            }
            已应用 = 0;
        }

        /// <summary>
        /// 以技能本体的累计值为准做幂等刷新：差值增量施加，避免重复叠加或漏加。
        /// </summary>
        private void 刷新(Character character)
        {
            double target = CSkill?.累计提升 ?? 0;
            double delta = target - 已应用;
            if (Math.Abs(delta) < 1e-9) return;
            switch (character.PrimaryAttribute)
            {
                case PrimaryAttribute.AGI:
                    character.ExAGI2 += delta;
                    break;
                case PrimaryAttribute.INT:
                    character.ExINT2 += delta;
                    break;
                default:
                    character.ExSTR2 += delta;
                    break;
            }
            已应用 = target;
        }
    }
}
