using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.ClassPlan;
using OshimaBot.Content.Skills;

namespace OshimaBot.Content.Classes
{
    /// <summary>
    /// 筽祀牻世界观职业内容：3 个职业，每个职业 2 个流派（共 6 组）
    /// <para/>世界观依据见 <c>docs_internal/筽祀牻-世界观.md</c>（来源 https://docs.milimoe.com/story/）
    /// <para/>· 职业取自大陆三方势力：铎京（科技中枢 · 秩序）、META🐴（熵之吞噬 · 混沌）、深海同盟（潮汐复仇 · 第三方）
    /// <para/>· 技能池一律从模组**通用战技 / 通用被动**中取材（<c>Skills/战技/</c> 与 <c>Skills/被动/</c>），
    ///   每职业 10 个战技 + 4 个被动：路线图共发放 8 次主动技能选择权、3 份「被动或数值提升」共享份额，池子略大于额度以保留取舍
    /// <para/>· 战斗天赋池按 <see cref="RoleType"/> 索引，每职业 4 个（核心 / 先锋 / 近卫 / 特色定位）
    /// <para/>· 流派提供 1 级与 6 级各 1 个固有被动，并按设定开放相应的角色定位
    /// <para/>· 职业模板属性上下限约束 1 级初始分配（30 点 + 3.0 成长）：导械师偏敏捷智力、熵噬者偏力量、深潮行者均衡偏力量
    /// <para/>· 号段：职业 7x01、流派 7x11 / 7x12、天赋 7x21–7x24、固有被动 7x31–7x34（x = 1 铎京 / 2 META🐴 / 3 深海）
    /// </summary>
    public static class OshimaWorldClasses
    {
        #region 铎京：导械师（7101）

        /// <summary>职业编号：导械师（铎京 · 导力机械与能量研究）</summary>
        public const long 导械师职业 = 7101;

        /// <summary>流派编号：晶枢术士（能量节点操控者）</summary>
        public const long 晶枢术士流派 = 7111;

        /// <summary>流派编号：铳械猎手（远程精准打击）</summary>
        public const long 铳械猎手流派 = 7112;

        /// <summary>天赋编号：核心·晶枢共鸣</summary>
        public const long 晶枢共鸣 = 7121;

        /// <summary>天赋编号：先锋·过载推进</summary>
        public const long 过载推进 = 7122;

        /// <summary>天赋编号：近卫·力场护盾</summary>
        public const long 力场护盾 = 7123;

        /// <summary>天赋编号：辅助·后勤补给</summary>
        public const long 后勤补给 = 7124;

        /// <summary>固有被动编号：能量感知（晶枢术士 · 1 级）</summary>
        public const long 能量感知 = 7131;

        /// <summary>固有被动编号：节点共鸣（晶枢术士 · 6 级）</summary>
        public const long 节点共鸣 = 7132;

        /// <summary>固有被动编号：枪械精通（铳械猎手 · 1 级）</summary>
        public const long 枪械精通 = 7133;

        /// <summary>固有被动编号：穿透弹头（铳械猎手 · 6 级）</summary>
        public const long 穿透弹头 = 7134;

        /// <summary>
        /// 创建职业「导械师」：铎京的导力技师，以精密机械、枪械与能量节点研究为业
        /// </summary>
        public static Class Create导械师()
        {
            Class definition = new() { Id = 导械师职业, Name = "导械师" };

            // 铎京技师：主敏捷与智力，力量受机械外骨骼限制
            definition.AttributeLimit = new ClassAttributeLimit(
                strMax: 16,
                agiMin: 8, agiMax: 24,
                intMin: 6, intMax: 22,
                strGrowthMax: 1.6,
                agiGrowthMax: 2.4,
                intGrowthMax: 2.0);

            // 技能池：战技（导力、枪械、机动、指挥）
            definition.Skills.Add(new 导力装甲 { Level = 1 });
            definition.Skills.Add(new 快速狙击 { Level = 1 });
            definition.Skills.Add(new 精准射击 { Level = 1 });
            definition.Skills.Add(new 跳跃点射 { Level = 1 });
            definition.Skills.Add(new 回复弹 { Level = 1 });
            definition.Skills.Add(new 闪现 { Level = 1 });
            definition.Skills.Add(new 疾走 { Level = 1 });
            definition.Skills.Add(new 号令 { Level = 1 });
            definition.Skills.Add(new 助威 { Level = 1 });
            definition.Skills.Add(new 挑拨 { Level = 1 });

            // 技能池：被动（先手、持续输出、机动、续航）
            definition.PassiveSkills.Add(new 先攻 { Level = 1 });
            definition.PassiveSkills.Add(new 致命节奏 { Level = 1 });
            definition.PassiveSkills.Add(new 迅捷步法 { Level = 1 });
            definition.PassiveSkills.Add(new 饼干配送 { Level = 1 });

            // 天赋池
            definition.CombatTalents[RoleType.Core] = [new OpenSkill(晶枢共鸣, "晶枢共鸣", []) { Level = 1 }];
            definition.CombatTalents[RoleType.Vanguard] = [new OpenSkill(过载推进, "过载推进", []) { Level = 1 }];
            definition.CombatTalents[RoleType.Guardian] = [new OpenSkill(力场护盾, "力场护盾", []) { Level = 1 }];
            definition.CombatTalents[RoleType.Support] = [new OpenSkill(后勤补给, "后勤补给", []) { Level = 1 }];

            return definition;
        }

        /// <summary>
        /// 创建流派「晶枢术士」：操控能量节点的铎京技术官，兼顾防护与支援
        /// </summary>
        public static SubClass Create晶枢术士(Class owner)
        {
            SubClass definition = new(owner) { Id = 晶枢术士流派, Name = "晶枢术士" };
            definition.RoleTypes.Add(RoleType.Core);
            definition.RoleTypes.Add(RoleType.Guardian);
            definition.RoleTypes.Add(RoleType.Support);
            definition.InherentPassives[1] = [new OpenSkill(能量感知, "能量感知", []) { Level = 1 }];
            definition.InherentPassives[6] = [new OpenSkill(节点共鸣, "节点共鸣", []) { Level = 1 }];
            return definition;
        }

        /// <summary>
        /// 创建流派「铳械猎手」：铎京军备体系下的远程射手，追求精准与输出
        /// </summary>
        public static SubClass Create铳械猎手(Class owner)
        {
            SubClass definition = new(owner) { Id = 铳械猎手流派, Name = "铳械猎手" };
            definition.RoleTypes.Add(RoleType.Core);
            definition.RoleTypes.Add(RoleType.Vanguard);
            definition.InherentPassives[1] = [new OpenSkill(枪械精通, "枪械精通", []) { Level = 1 }];
            definition.InherentPassives[6] = [new OpenSkill(穿透弹头, "穿透弹头", []) { Level = 1 }];
            return definition;
        }

        #endregion

        #region META🐴：熵噬者（7201）

        /// <summary>职业编号：熵噬者（META🐴 · 信奉「力量即真理」，驾驭熵之力）</summary>
        public const long 熵噬者职业 = 7201;

        /// <summary>流派编号：熵灭剑徒（承袭大岛シヤ双手剑「熵灭」的剑之道路）</summary>
        public const long 熵灭剑徒流派 = 7211;

        /// <summary>流派编号：实验体（META🐴 人体实验的成品，以极致回复换取失控力量）</summary>
        public const long 实验体流派 = 7212;

        /// <summary>天赋编号：核心·熵灭回响</summary>
        public const long 熵灭回响 = 7221;

        /// <summary>天赋编号：先锋·噬能突进</summary>
        public const long 噬能突进 = 7222;

        /// <summary>天赋编号：近卫·熵壁</summary>
        public const long 熵壁 = 7223;

        /// <summary>天赋编号：医疗·熵愈（掠夺他人生气填补自身，呼应主角「流」的极致回复）</summary>
        public const long 熵愈 = 7224;

        /// <summary>固有被动编号：熵之亲和（熵灭剑徒 · 1 级）</summary>
        public const long 熵之亲和 = 7231;

        /// <summary>固有被动编号：剑势·灭（熵灭剑徒 · 6 级）</summary>
        public const long 剑势灭 = 7232;

        /// <summary>固有被动编号：再生因子（实验体 · 1 级）</summary>
        public const long 再生因子 = 7233;

        /// <summary>固有被动编号：熵核觉醒（实验体 · 6 级）</summary>
        public const long 熵核觉醒 = 7234;

        /// <summary>
        /// 创建职业「熵噬者」：META🐴 的战斗人员，通过吞噬能量换取压倒性的破坏力
        /// </summary>
        public static Class Create熵噬者()
        {
            Class definition = new() { Id = 熵噬者职业, Name = "熵噬者" };

            // 熵噬者：主力量，智力被熵之力侵蚀而受限
            definition.AttributeLimit = new ClassAttributeLimit(
                strMin: 10, strMax: 26,
                agiMax: 18,
                intMax: 12,
                strGrowthMax: 2.6,
                agiGrowthMax: 1.8,
                intGrowthMax: 1.0);

            // 技能池：战技（剑技、收割、暗影、灾厄）
            definition.Skills.Add(new 千剑之雨 { Level = 1 });
            definition.Skills.Add(new 无尽剑制 { Level = 1 });
            definition.Skills.Add(new 光破斩 { Level = 1 });
            definition.Skills.Add(new 光鬼斩 { Level = 1 });
            definition.Skills.Add(new 断罪斩 { Level = 1 });
            definition.Skills.Add(new 血腥旋转 { Level = 1 });
            definition.Skills.Add(new 死亡制裁 { Level = 1 });
            definition.Skills.Add(new 灾难一掷 { Level = 1 });
            definition.Skills.Add(new 绝影 { Level = 1 });
            definition.Skills.Add(new 魔眼 { Level = 1 });

            // 技能池：被动（征服、收割、贪欲、爆发）
            definition.PassiveSkills.Add(new 征服者 { Level = 1 });
            definition.PassiveSkills.Add(new 黑暗收割 { Level = 1 });
            definition.PassiveSkills.Add(new 贪欲猎手 { Level = 1 });
            definition.PassiveSkills.Add(new 电刑 { Level = 1 });

            // 天赋池
            definition.CombatTalents[RoleType.Core] = [new OpenSkill(熵灭回响, "熵灭回响", []) { Level = 1 }];
            definition.CombatTalents[RoleType.Vanguard] = [new OpenSkill(噬能突进, "噬能突进", []) { Level = 1 }];
            definition.CombatTalents[RoleType.Guardian] = [new OpenSkill(熵壁, "熵壁", []) { Level = 1 }];
            definition.CombatTalents[RoleType.Medic] = [new OpenSkill(熵愈, "熵愈", []) { Level = 1 }];

            return definition;
        }

        /// <summary>
        /// 创建流派「熵灭剑徒」：以剑承载熵之力，追求极致的歼灭
        /// </summary>
        public static SubClass Create熵灭剑徒(Class owner)
        {
            SubClass definition = new(owner) { Id = 熵灭剑徒流派, Name = "熵灭剑徒" };
            definition.RoleTypes.Add(RoleType.Core);
            definition.RoleTypes.Add(RoleType.Vanguard);
            definition.RoleTypes.Add(RoleType.Guardian);
            definition.InherentPassives[1] = [new OpenSkill(熵之亲和, "熵之亲和", []) { Level = 1 }];
            definition.InherentPassives[6] = [new OpenSkill(剑势灭, "剑势·灭", []) { Level = 1 }];
            return definition;
        }

        /// <summary>
        /// 创建流派「实验体」：META🐴 人体实验的成品，拥有极致回复能力，代价是体内熵之力持续失控
        /// </summary>
        public static SubClass Create实验体(Class owner)
        {
            SubClass definition = new(owner) { Id = 实验体流派, Name = "实验体" };
            definition.RoleTypes.Add(RoleType.Core);
            definition.RoleTypes.Add(RoleType.Guardian);
            definition.RoleTypes.Add(RoleType.Medic);
            definition.InherentPassives[1] = [new OpenSkill(再生因子, "再生因子", []) { Level = 1 }];
            definition.InherentPassives[6] = [new OpenSkill(熵核觉醒, "熵核觉醒", []) { Level = 1 }];
            return definition;
        }

        #endregion

        #region 深海同盟：深潮行者（7301）

        /// <summary>职业编号：深潮行者（深海同盟 · 驾驭潮汐与海兽之力的复仇者）</summary>
        public const long 深潮行者职业 = 7301;

        /// <summary>流派编号：潮汐守卫（以肉身与潮汐抵御冲击的守护者）</summary>
        public const long 潮汐守卫流派 = 7311;

        /// <summary>流派编号：深渊猎手（在暗流中猎杀目标的刺杀者）</summary>
        public const long 深渊猎手流派 = 7312;

        /// <summary>天赋编号：核心·潮汐共鸣</summary>
        public const long 潮汐共鸣 = 7321;

        /// <summary>天赋编号：先锋·暗流突袭</summary>
        public const long 暗流突袭 = 7322;

        /// <summary>天赋编号：近卫·深渊庇佑</summary>
        public const long 深渊庇佑 = 7323;

        /// <summary>天赋编号：医疗·生命之泉（源自筽祀牻之眼的净化之力）</summary>
        public const long 生命之泉 = 7324;

        /// <summary>固有被动编号：潮汐之躯（潮汐守卫 · 1 级）</summary>
        public const long 潮汐之躯 = 7331;

        /// <summary>固有被动编号：海兽庇佑（潮汐守卫 · 6 级）</summary>
        public const long 海兽庇佑 = 7332;

        /// <summary>固有被动编号：深渊感知（深渊猎手 · 1 级）</summary>
        public const long 深渊感知 = 7333;

        /// <summary>固有被动编号：猎杀本能（深渊猎手 · 6 级）</summary>
        public const long 猎杀本能 = 7334;

        /// <summary>
        /// 创建职业「深潮行者」：深海同盟的战士，以海兽般的体魄与洋流操控立足战场
        /// </summary>
        public static Class Create深潮行者()
        {
            Class definition = new() { Id = 深潮行者职业, Name = "深潮行者" };

            // 深潮行者：力量为主、敏捷次之，深海环境限制了智力发展
            definition.AttributeLimit = new ClassAttributeLimit(
                strMin: 8, strMax: 24,
                agiMax: 20,
                intMax: 16,
                strGrowthMax: 2.4,
                agiGrowthMax: 2.0,
                intGrowthMax: 1.4);

            // 技能池：战技（锚击、鞭缚、洋流、回旋）
            definition.Skills.Add(new 鲨鱼锚击 { Level = 1 });
            definition.Skills.Add(new 雷索吸缚 { Level = 1 });
            definition.Skills.Add(new 拘束之鞭 { Level = 1 });
            definition.Skills.Add(new 风之鞭 { Level = 1 });
            definition.Skills.Add(new 岚 { Level = 1 });
            definition.Skills.Add(new 螺旋之刃 { Level = 1 });
            definition.Skills.Add(new 陀螺舞 { Level = 1 });
            definition.Skills.Add(new 落叶 { Level = 1 });
            definition.Skills.Add(new 狂刃剑舞 { Level = 1 });
            definition.Skills.Add(new 旋风轮 { Level = 1 });

            // 技能池：被动（深海外壳、重击、坚韧、反击）
            definition.PassiveSkills.Add(new 海妖外壳 { Level = 1 });
            definition.PassiveSkills.Add(new 深海重击 { Level = 1 });
            definition.PassiveSkills.Add(new 不灭之握 { Level = 1 });
            definition.PassiveSkills.Add(new 反击螺旋 { Level = 1 });

            // 天赋池
            definition.CombatTalents[RoleType.Core] = [new OpenSkill(潮汐共鸣, "潮汐共鸣", []) { Level = 1 }];
            definition.CombatTalents[RoleType.Vanguard] = [new OpenSkill(暗流突袭, "暗流突袭", []) { Level = 1 }];
            definition.CombatTalents[RoleType.Guardian] = [new OpenSkill(深渊庇佑, "深渊庇佑", []) { Level = 1 }];
            definition.CombatTalents[RoleType.Medic] = [new OpenSkill(生命之泉, "生命之泉", []) { Level = 1 }];

            return definition;
        }

        /// <summary>
        /// 创建流派「潮汐守卫」：如利维坦般承受冲击，守护同袍与海域
        /// </summary>
        public static SubClass Create潮汐守卫(Class owner)
        {
            SubClass definition = new(owner) { Id = 潮汐守卫流派, Name = "潮汐守卫" };
            definition.RoleTypes.Add(RoleType.Guardian);
            definition.RoleTypes.Add(RoleType.Vanguard);
            definition.RoleTypes.Add(RoleType.Medic);
            definition.InherentPassives[1] = [new OpenSkill(潮汐之躯, "潮汐之躯", []) { Level = 1 }];
            definition.InherentPassives[6] = [new OpenSkill(海兽庇佑, "海兽庇佑", []) { Level = 1 }];
            return definition;
        }

        /// <summary>
        /// 创建流派「深渊猎手」：潜行于暗流之中，一击定生死
        /// </summary>
        public static SubClass Create深渊猎手(Class owner)
        {
            SubClass definition = new(owner) { Id = 深渊猎手流派, Name = "深渊猎手" };
            definition.RoleTypes.Add(RoleType.Core);
            definition.RoleTypes.Add(RoleType.Vanguard);
            definition.InherentPassives[1] = [new OpenSkill(深渊感知, "深渊感知", []) { Level = 1 }];
            definition.InherentPassives[6] = [new OpenSkill(猎杀本能, "猎杀本能", []) { Level = 1 }];
            return definition;
        }

        #endregion

        /// <summary>
        /// 把本文件的职业 / 流派注册到核心库定义注册表（进程启动时调用一次）
        /// <para/>共 3 职业 × 2 流派 = 6 组；【转换战斗天赋】战技复用 <see cref="OshimaClasses"/> 的全局注册，此处不重复注册
        /// </summary>
        public static void RegisterAll()
        {
            ClassDefinitionRegistry.Register(Create导械师, Create晶枢术士);
            ClassDefinitionRegistry.Register(Create导械师, Create铳械猎手);
            ClassDefinitionRegistry.Register(Create熵噬者, Create熵灭剑徒);
            ClassDefinitionRegistry.Register(Create熵噬者, Create实验体);
            ClassDefinitionRegistry.Register(Create深潮行者, Create潮汐守卫);
            ClassDefinitionRegistry.Register(Create深潮行者, Create深渊猎手);
        }
    }
}
