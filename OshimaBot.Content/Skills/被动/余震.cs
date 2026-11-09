using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;
using OshimaBot.Content.Effects.PassiveEffects;

namespace OshimaBot.Content.Skills
{
    public class 余震 : Skill
    {
        public override long Id => (long)PassiveID.余震;
        public override string Name => "余震";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 余震(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 余震特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 余震特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"对处于控制状态的目标造成伤害时，获得 {双抗提升 * 100:0.##}% 物理护甲与魔法抗性加成，持续 {强化持续时间:0.##} {GameplayEquilibriumConstant.InGameTime}，并溅射 {溅射伤害:0.##} 点魔法伤害至随机敌人（{溅射目标数} 个，不含目标自身）。触发后进入 {冷却时间:0.##} {GameplayEquilibriumConstant.InGameTime}冷却。";

        private double 剩余冷却 = 0;

        // 平衡调整（2026-09-22）：CD Lv60 9.6s → 12s（Admin：余震 CD 偏短）
        private double 冷却时间 => Skill.Character != null ? 18 - Skill.Character.Level * 0.1 : 18;
        private double 强化持续时间 => Skill.Character != null ? 5 + Skill.Character.Level * 0.1 : 5;
        private double 双抗提升 => Skill.Character != null ? 0.15 + Skill.Character.Level * 0.002 : 0.15;
        // 平衡调整（2026-09-22）：Lv60 溅射 559.65 → 427.72（−24%）（Admin：余震数值稍高）
        private double 溅射伤害 => Skill.Character != null ? 80 + Skill.Character.Level * 5 + Skill.Character.PrimaryAttributeValue * 0.4 : 80;
        private int 溅射目标数 => 2;

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character || ctx.Enemy is not Character enemy) return;
            if (Skill.Character == null || Skill.Character != character) return;
            if (ctx.DamageResult != DamageResult.Normal && ctx.DamageResult != DamageResult.Critical) return;
            if (剩余冷却 > 0) return;
            if (!IsControl(enemy)) return;
            剩余冷却 = 冷却时间;
            WriteLine($"[ {character} ] 发动了余震！目标 [ {enemy} ] 处于控制状态，自身获得双抗加成！");
            Effect e = new DynamicsEffect(Skill, new Dictionary<string, object>()
            {
                { "exdef2", 双抗提升 },
                { "mdftype", 0 },
                { "mdfvalue", 双抗提升 }
            }, character)
            {
                Name = nameof(余震) + "·双抗",
                Durative = true,
                Duration = 强化持续时间
            };
            character.Effects.Add(e);
            e.OnEffectGained(new HookContext(GamingQueue, character));

            foreach (Character target in RandomTargets(character, enemy, 溅射目标数))
            {
                WriteLine($"[ {character} ] 的余震溅射到了 [ {target} ]，造成 {溅射伤害:0.##} 点魔法伤害！");
                DamageToEnemy(character, target, DamageType.Magical, MagicType.None, 溅射伤害, new(character)
                {
                    TriggerEffects = false
                });
            }
        }

        public override void OnTimeElapsed(TimeLapseContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (剩余冷却 > 0)
            {
                剩余冷却 -= ctx.Elapsed;
            }
        }

        private static bool IsControl(Character character)
        {
            if (character.CharacterState is CharacterState.NotActionable or CharacterState.ActionRestricted
                or CharacterState.BattleRestricted or CharacterState.SkillRestricted or CharacterState.AttackRestricted)
            {
                return true;
            }
            return character.Effects.Any(e => e is 眩晕 or 气绝 or 冻结 or 石化 or 混乱 or 战斗不能 or 缴械 or 封技 or 完全行动不能);
        }

        private IEnumerable<Character> RandomTargets(Character self, Character exclude, int count)
        {
            return GamingQueue?.AllCharacters
                .Where(c => c != self && c != exclude && c.HP > 0)
                .OrderBy(_ => Random.Next())
                .Take(count) ?? [];
        }
    }
}
