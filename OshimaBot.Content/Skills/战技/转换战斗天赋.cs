using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.PrefabricatedEntity;

namespace OshimaBot.Content.Skills
{
    /// <summary>
    /// 【转换战斗天赋】战技：战斗内切换到另一已学战斗天赋
    /// <para>继承核心库预制基类 <see cref="SwitchCombatTalentSkill"/>，本类只提供编号与表现参数；
    /// 技能类型（战技）与决策点 / 配额行为沿用核心库既有逻辑，不做改动</para>
    /// </summary>
    public class 转换战斗天赋 : SwitchCombatTalentSkill
    {
        public override long Id => (long)SkillID.转换战斗天赋;
        public override string Name => "转换战斗天赋";
        public override string Description => "战斗内切换到另一已学的战斗天赋，取消当前生效的天赋；目标定位未指定时自动切到当前未激活的第一个已学天赋。";
        public override double EPCost => 0;
        public override double CD => 3;
        public override double HardnessTime { get; set; } = 2;

        /// <summary>
        /// 创建【转换战斗天赋】战技实例
        /// </summary>
        /// <param name="character">所属角色（装配时由挂载流程赋值）</param>
        /// <param name="targetRoleType">目标定位；null 时由内核自动选择当前未激活的已学天赋</param>
        public 转换战斗天赋(Character? character = null, RoleType? targetRoleType = null) : base(character)
        {
            TargetRoleType = targetRoleType;
            CanSelectSelf = true;
            CanSelectEnemy = false;
            CanSelectTeammate = false;
            CanSelectTargetRange = 0;
            IsNonDirectional = true;
            SelectIncludeCharacterGrid = false;
            AllowSelectNoCharacterGrid = true;
        }
    }
}
