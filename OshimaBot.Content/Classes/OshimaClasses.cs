using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.ClassPlan;
using FunGame.Core.Model.Framework;
using OshimaBot.Content.Skills;

namespace OshimaBot.Content.Classes
{
    /// <summary>
    /// 首个真实职业内容：职业「剑士」与流派「剑豪」
    /// <para/>· 技能池：4 个真实战技 + 2 个被动，供路线图「职业技能选择权」消耗
    /// <para/>· 天赋池：核心 / 先锋 / 近卫 三系（核心天赋由内核自动附加「全等级 +1」）
    /// <para/>· 固有被动：1 级与 6 级各 1 个，与路线图 InherentPassive 档位一致
    /// <para/>· 职业模板属性上下限：约束 1 级初始分配（30 点 + 3.0 成长）
    /// <para/>· 路线图数值：沿用 <see cref="EquilibriumConstant.ClassLevelUpRewards"/> 默认表，不做职业专属覆盖
    /// </summary>
    public static class OshimaClasses
    {
        /// <summary>职业编号：剑士</summary>
        public const long 剑士职业 = 7001;

        /// <summary>流派编号：剑豪</summary>
        public const long 剑豪流派 = 7002;

        /// <summary>天赋编号：核心·剑心通明</summary>
        public const long 剑心通明 = 7010;

        /// <summary>天赋编号：先锋·疾风剑势</summary>
        public const long 疾风剑势 = 7011;

        /// <summary>天赋编号：近卫·铁壁架势</summary>
        public const long 铁壁架势 = 7012;

        /// <summary>天赋编号：核心·剑意凝聚（与剑心通明同定位，用于验证同定位多天赋）</summary>
        public const long 剑意凝聚 = 7013;

        /// <summary>固有被动编号：剑士体魄（1 级）</summary>
        public const long 剑士体魄 = 7020;

        /// <summary>固有被动编号：剑豪意志（6 级）</summary>
        public const long 剑豪意志 = 7021;

        /// <summary>
        /// 创建职业「剑士」定义（每次返回新实例，供规划器 Copy 为玩家职业记录）
        /// </summary>
        public static Class Create剑士()
        {
            Class definition = new() { Id = 剑士职业, Name = "剑士" };

            // 职业模板属性上下限：1 级初始分配必须落在该区间内（与角色模板限值取交集）
            definition.AttributeLimit = new ClassAttributeLimit(
                strMin: 8, strMax: 24,
                agiMax: 16,
                intMax: 10,
                strGrowthMax: 2.4,
                agiGrowthMax: 1.6,
                intGrowthMax: 1.0);

            // 技能池：战技
            definition.Skills.Add(new 绞丝棍 { Level = 1 });
            definition.Skills.Add(new 金刚击 { Level = 1 });
            definition.Skills.Add(new 旋风轮 { Level = 1 });
            definition.Skills.Add(new 双连击 { Level = 1 });

            // 技能池：被动（消耗被动选择权习得）
            definition.PassiveSkills.Add(new 强攻 { Level = 1 });
            definition.PassiveSkills.Add(new 征服者 { Level = 1 });

            // 天赋池：按定位索引，战斗天赋绑定职业；同一定位可提供多个候选
            definition.CombatTalents[RoleType.Core] =
            [
                new OpenSkill(剑心通明, "剑心通明", []) { Level = 1 },
                new OpenSkill(剑意凝聚, "剑意凝聚", []) { Level = 1 }
            ];
            definition.CombatTalents[RoleType.Vanguard] = [new OpenSkill(疾风剑势, "疾风剑势", []) { Level = 1 }];
            definition.CombatTalents[RoleType.Guardian] = [new OpenSkill(铁壁架势, "铁壁架势", []) { Level = 1 }];

            return definition;
        }

        /// <summary>
        /// 创建流派「剑豪」定义（提供核心 / 先锋 / 近卫定位候选与 1 / 6 级固有被动）
        /// </summary>
        /// <param name="owner">所属职业记录（流派等级委托给该职业）</param>
        public static SubClass Create剑豪(Class owner)
        {
            SubClass definition = new(owner) { Id = 剑豪流派, Name = "剑豪" };
            definition.RoleTypes.Add(RoleType.Core);
            definition.RoleTypes.Add(RoleType.Vanguard);
            definition.RoleTypes.Add(RoleType.Guardian);
            definition.InherentPassives[1] = [new OpenSkill(剑士体魄, "剑士体魄", []) { Level = 1 }];
            definition.InherentPassives[6] = [new OpenSkill(剑豪意志, "剑豪意志", []) { Level = 1 }];
            return definition;
        }

        /// <summary>
        /// 创建【转换战斗天赋】战技实例，交给 <see cref="FunGame.Core.Model.ClassPlanner.SetCombatTalentSwitchSkill"/> 注入
        /// </summary>
        /// <param name="targetRoleType">目标定位；null 时由内核自动选择当前未激活的已学天赋</param>
        public static Skill Create转换战斗天赋(RoleType? targetRoleType = null)
        {
            return new 转换战斗天赋(null, targetRoleType);
        }

        /// <summary>
        /// 把本模块的职业 / 流派 / 转换战斗天赋战技注册到核心库定义注册表（进程启动时调用一次）
        /// <para>注册后职业计划存档才能按 IdName 重建职业与流派记录</para>
        /// </summary>
        public static void RegisterAll()
        {
            ClassDefinitionRegistry.Register(Create剑士, Create剑豪);
            ClassDefinitionRegistry.RegisterSwitchSkill(Create转换战斗天赋().GetIdName(), () => Create转换战斗天赋());
        }
    }
}
