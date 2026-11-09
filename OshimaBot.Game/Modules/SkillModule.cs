using OshimaBot.Game.Constants;
using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.ItemEffects;
using OshimaBot.Content.Effects.OpenEffects;
using OshimaBot.Content.Items;
using OshimaBot.Content.Skills;
using SM = FunGame.Core.Library.Module.SkillModule;

namespace OshimaBot.Game.Modules
{
    public class SkillModule : SM
    {
        public override string Name => OshimaGameModuleConstant.Skill;
        public override string Description => OshimaGameModuleConstant.Description;
        public override string Version => OshimaGameModuleConstant.Version;
        public override string Author => OshimaGameModuleConstant.Author;
        public Dictionary<string, Skill> KnownSkills { get; } = [];

        public override Dictionary<string, Skill> Skills
        {
            get
            {
                Dictionary<string, Skill> skills = Factory.GetGameModuleInstances<Skill>(OshimaGameModuleConstant.General, OshimaGameModuleConstant.Skill);
                if (KnownSkills.Count == 0 && skills.Count > 0)
                {
                    foreach (string key in skills.Keys)
                    {
                        KnownSkills[key] = skills[key];
                    }
                }
                return skills;
            }
        }

        protected override void AfterLoad()
        {
            General.GameplayEquilibriumConstant.InGameTime = "秒";
            General.GameplayEquilibriumConstant.InGameMaterial = "钻石";
            General.GameplayEquilibriumConstant.UseMagicType = [MagicType.None];
        }

        protected override Factory.EntityFactoryDelegate<Skill> SkillFactory()
        {
            return (id, name, args) =>
            {
                return id switch
                {
                    (long)MagicID.冰霜攻击 => new 冰霜攻击(),
                    (long)MagicID.火之矢 => new 火之矢(),
                    (long)MagicID.水之矢 => new 水之矢(),
                    (long)MagicID.石之锤 => new 石之锤(),
                    (long)MagicID.风之轮 => new 风之轮(),
                    (long)MagicID.心灵之霞 => new 心灵之霞(),
                    (long)MagicID.次元上升 => new 次元上升(),
                    (long)MagicID.暗物质 => new 暗物质(),
                    (long)MagicID.回复术 => new 回复术(),
                    (long)MagicID.治愈术 => new 治愈术(),
                    (long)MagicID.复苏术 => new 复苏术(),
                    (long)MagicID.圣灵术 => new 圣灵术(),
                    (long)MagicID.时间加速 => new 时间加速(),
                    (long)MagicID.时间减速 => new 时间减速(),
                    (long)MagicID.反魔法领域 => new 反魔法领域(),
                    (long)MagicID.沉默十字 => new 沉默十字(),
                    (long)MagicID.虚弱领域 => new 虚弱领域(),
                    (long)MagicID.混沌烙印 => new 混沌烙印(),
                    (long)MagicID.凝胶稠絮 => new 凝胶稠絮(),
                    (long)MagicID.大地之墙 => new 大地之墙(),
                    (long)MagicID.盖亚之盾 => new 盖亚之盾(),
                    (long)MagicID.风之守护 => new 风之守护(),
                    (long)MagicID.结晶防护 => new 结晶防护(),
                    (long)MagicID.强音之力 => new 强音之力(),
                    (long)MagicID.神圣祝福 => new 神圣祝福(),
                    (long)MagicID.根源屏障 => new 根源屏障(),
                    (long)MagicID.灾难冲击波 => new 灾难冲击波(),
                    (long)MagicID.银色荆棘 => new 银色荆棘(),
                    (long)MagicID.等离子之波 => new 等离子之波(),
                    (long)MagicID.地狱之门 => new 地狱之门(),
                    (long)MagicID.钻石星尘 => new 钻石星尘(),
                    (long)MagicID.死亡咆哮 => new 死亡咆哮(),
                    (long)MagicID.鬼魅之痛 => new 鬼魅之痛(),
                    (long)MagicID.导力停止 => new 导力停止(),
                    (long)MagicID.冰狱冥嚎 => new 冰狱冥嚎(),
                    (long)MagicID.火山咆哮 => new 火山咆哮(),
                    (long)MagicID.水蓝轰炸 => new 水蓝轰炸(),
                    (long)MagicID.岩石之息 => new 岩石之息(),
                    (long)MagicID.弧形日珥 => new 弧形日珥(),
                    (long)MagicID.苍白地狱 => new 苍白地狱(),
                    (long)MagicID.破碎虚空 => new 破碎虚空(),
                    (long)MagicID.弧光消耗 => new 弧光消耗(),
                    (long)MagicID.回复术改 => new 回复术改(),
                    (long)MagicID.回复术复 => new 回复术复(),
                    (long)MagicID.治愈术复 => new 治愈术复(),
                    (long)MagicID.风之守护复 => new 风之守护复(),
                    (long)MagicID.强音之力复 => new 强音之力复(),
                    (long)MagicID.结晶防护复 => new 结晶防护复(),
                    (long)MagicID.神圣祝福复 => new 神圣祝福复(),
                    (long)MagicID.时间加速改 => new 时间加速改(),
                    (long)MagicID.时间减速改 => new 时间减速改(),
                    (long)MagicID.时间加速复 => new 时间加速复(),
                    (long)MagicID.时间减速复 => new 时间减速复(),
                    (long)MagicID.十二宫星环 => new 十二宫星环(),
                    (long)MagicID.时间剥夺 => new 时间剥夺(),
                    (long)SkillID.疾风步 => new 疾风步(),
                    (long)SkillID.助威 => new 助威(),
                    (long)SkillID.挑拨 => new 挑拨(),
                    (long)SkillID.绞丝棍 => new 绞丝棍(),
                    (long)SkillID.金刚击 => new 金刚击(),
                    (long)SkillID.旋风轮 => new 旋风轮(),
                    (long)SkillID.双连击 => new 双连击(),
                    (long)SkillID.绝影 => new 绝影(),
                    (long)SkillID.胧 => new 胧(),
                    (long)SkillID.魔眼 => new 魔眼(),
                    (long)SkillID.天堂之吻 => new 天堂之吻(),
                    (long)SkillID.回复弹 => new 回复弹(),
                    (long)SkillID.养命功 => new 养命功(),
                    (long)SkillID.镜花水月 => new 镜花水月(),
                    (long)SkillID.剑风闪 => new 剑风闪(),
                    (long)SkillID.鲨鱼锚击 => new 鲨鱼锚击(),
                    (long)SkillID.疾走 => new 疾走(),
                    (long)SkillID.闪现 => new 闪现(),
                    (long)SkillID.风之鞭 => new 风之鞭(),
                    (long)SkillID.拘束之鞭 => new 拘束之鞭(),
                    (long)SkillID.狐媚暗随 => new 狐媚暗随(),
                    (long)SkillID.快速狙击 => new 快速狙击(),
                    (long)SkillID.精准射击 => new 精准射击(),
                    (long)SkillID.欢乐激发 => new 欢乐激发(),
                    (long)SkillID.乱心安魂曲 => new 乱心安魂曲(),
                    (long)SkillID.斗魂 => new 斗魂(),
                    (long)SkillID.岚 => new 岚(),
                    (long)SkillID.公牛之怒 => new 公牛之怒(),
                    (long)SkillID.火焰碎击 => new 火焰碎击(),
                    (long)SkillID.螺旋之刃 => new 螺旋之刃(),
                    (long)SkillID.霸王疾风 => new 霸王疾风(),
                    (long)SkillID.导力装甲 => new 导力装甲(),
                    (long)SkillID.回复原状 => new 回复原状(),
                    (long)SkillID.强打 => new 强打(),
                    (long)SkillID.龙神功 => new 龙神功(),
                    (long)SkillID.月华掌 => new 月华掌(),
                    (long)SkillID.雷神脚 => new 雷神脚(),
                    (long)SkillID.弓刃交错 => new 弓刃交错(),
                    (long)SkillID.神圣祈祷 => new 神圣祈祷(),
                    (long)SkillID.牺牲之箭 => new 牺牲之箭(),
                    (long)SkillID.石化之矢 => new 石化之矢(),
                    (long)SkillID.死亡制裁 => new 死亡制裁(),
                    (long)SkillID.落叶 => new 落叶(),
                    (long)SkillID.风花阵 => new 风花阵(),
                    (long)SkillID.陀螺舞 => new 陀螺舞(),
                    (long)SkillID.光破斩 => new 光破斩(),
                    (long)SkillID.跳跃点射 => new 跳跃点射(),
                    (long)SkillID.光鬼斩 => new 光鬼斩(),
                    (long)SkillID.雷索吸缚 => new 雷索吸缚(),
                    (long)SkillID.狂刃剑舞 => new 狂刃剑舞(),
                    (long)SkillID.无相飞刀 => new 无相飞刀(),
                    (long)SkillID.破邪显正 => new 破邪显正(),
                    (long)SkillID.号令 => new 号令(),
                    (long)SkillID.千剑之雨 => new 千剑之雨(),
                    (long)SkillID.无尽剑制 => new 无尽剑制(),
                    (long)SkillID.灾难一掷 => new 灾难一掷(),
                    (long)SkillID.血腥旋转 => new 血腥旋转(),
                    (long)SkillID.断罪斩 => new 断罪斩(),
                    // 决策点维度（2026-09-23 新增）
                    (long)SkillID.战术部署 => new 战术部署(),
                    (long)SkillID.过载 => new 过载(),
                    (long)SuperSkillID.催战鼓 => new 催战鼓(),
                    (long)SkillID.抢夺命运 => new 抢夺命运(),
                    (long)SkillID.命运剥夺 => new 命运剥夺(),
                    (long)SkillID.命运馈赠 => new 命运馈赠(),
                    (long)SkillID.命运之赐 => new 命运之赐(),
                    (long)SuperSkillID.熵灭极诣 => new 熵灭极诣(),
                    (long)SuperSkillID.千羽瞬华 => new 千羽瞬华(),
                    (long)SuperSkillID.咒怨洪流 => new 咒怨洪流(),
                    (long)SuperSkillID.三相灵枢 => new 三相灵枢(),
                    (long)SuperSkillID.变幻之心 => new 变幻之心(),
                    (long)SuperSkillID.零式灭杀 => new 零式灭杀(),
                    (long)SuperSkillID.绝对领域 => new 绝对领域(),
                    (long)SuperSkillID.残香凋零 => new 残香凋零(),
                    (long)SuperSkillID.宿命时律 => new 宿命时律(),
                    (long)SuperSkillID.极寒渴望 => new 极寒渴望(),
                    (long)SuperSkillID.身心一境 => new 身心一境(),
                    (long)SuperSkillID.饕餮盛宴 => new 饕餮盛宴(),
                    (long)SuperSkillID.放监 => new 放监(),
                    (long)SuperSkillID.归元环 => new 归元环(),
                    (long)SuperSkillID.海王星的野望 => new 海王星的野望(),
                    (long)SuperSkillID.全军出击 => new 全军出击(),
                    (long)SuperSkillID.宿命之潮 => new 宿命之潮(),
                    (long)SuperSkillID.神之因果 => new 神之因果(),
                    (long)SuperSkillID.樱花无双击 => new 樱花无双击(),
                    (long)SuperSkillID.漆黑之牙 => new 漆黑之牙(),
                    (long)SuperSkillID.女王之怒 => new 女王之怒(),
                    (long)SuperSkillID.裁决塔罗 => new 裁决塔罗(),
                    (long)SuperSkillID.光明之环 => new 光明之环(),
                    (long)SuperSkillID.圣星光旋 => new 圣星光旋(),
                    (long)SuperSkillID.炎龙倒海 => new 炎龙倒海(),
                    (long)SuperSkillID.卫星激光 => new 卫星激光(),
                    (long)SuperSkillID.泰山玄武靠 => new 泰山玄武靠(),
                    (long)SuperSkillID.星杯领域 => new 星杯领域(),
                    (long)SuperSkillID.魔枪洛亚 => new 魔枪洛亚(),
                    (long)SuperSkillID.八叶灭杀 => new 八叶灭杀(),
                    (long)SuperSkillID.樱花残月 => new 樱花残月(),
                    (long)SuperSkillID.圣洁祝福 => new 圣洁祝福(),
                    (long)SuperSkillID.天堂阻灭 => new 天堂阻灭(),
                    (long)SuperSkillID.歼灭 => new 歼灭(),
                    (long)PassiveID.META马 => new META马(),
                    (long)PassiveID.心灵之弦 => new 心灵之弦(),
                    (long)PassiveID.蚀魂震击 => new 蚀魂震击(),
                    (long)PassiveID.灵能反射 => new 灵能反射(),
                    (long)PassiveID.双生流转 => new 双生流转(),
                    (long)PassiveID.零式崩解 => new 零式崩解(),
                    (long)PassiveID.少女绮想 => new 少女绮想(),
                    (long)PassiveID.暗香疏影 => new 暗香疏影(),
                    (long)PassiveID.破釜沉舟 => new 破釜沉舟(),
                    (long)PassiveID.累积之压 => new 累积之压(),
                    (long)PassiveID.银隼之赐 => new 银隼之赐(),
                    (long)PassiveID.弱者猎手 => new 弱者猎手(),
                    (long)PassiveID.开宫 => new 开宫(),
                    (long)PassiveID.八卦阵 => new 八卦阵(),
                    (long)PassiveID.深海之戟 => new 深海之戟(),
                    (long)PassiveID.概念之骰 => new 概念之骰(),
                    (long)PassiveID.掠夺者 => new 掠夺者(),
                    (long)PassiveID.雇佣兵团 => new 雇佣兵团(),
                    (long)PassiveID.不息之流 => new 不息之流(),
                    (long)PassiveID.征服者 => new 征服者(),
                    (long)PassiveID.致命节奏 => new 致命节奏(),
                    (long)PassiveID.强攻 => new 强攻(),
                    (long)PassiveID.电刑 => new 电刑(),
                    (long)PassiveID.黑暗收割 => new 黑暗收割(),
                    (long)PassiveID.迅捷步法 => new 迅捷步法(),
                    (long)PassiveID.贪欲猎手 => new 贪欲猎手(),
                    (long)PassiveID.丛刃 => new 丛刃(),
                    (long)PassiveID.召唤艾黎 => new 召唤艾黎(),
                    (long)PassiveID.相位猛冲 => new 相位猛冲(),
                    (long)PassiveID.奥术彗星 => new 奥术彗星(),
                    (long)PassiveID.风暴聚集 => new 风暴聚集(),
                    (long)PassiveID.不灭之握 => new 不灭之握(),
                    (long)PassiveID.余震 => new 余震(),
                    (long)PassiveID.守护者 => new 守护者(),
                    (long)PassiveID.骸骨镀层 => new 骸骨镀层(),
                    (long)PassiveID.冰川增幅 => new 冰川增幅(),
                    (long)PassiveID.先攻 => new 先攻(),
                    (long)PassiveID.饼干配送 => new 饼干配送(),
                    (long)PassiveID.折射 => new 折射(),
                    (long)PassiveID.恩赐解脱 => new 恩赐解脱(),
                    (long)PassiveID.静电场 => new 静电场(),
                    (long)PassiveID.竭心光环 => new 竭心光环(),
                    (long)PassiveID.海妖外壳 => new 海妖外壳(),
                    (long)PassiveID.自然蔽护 => new 自然蔽护(),
                    (long)PassiveID.勇气之霎 => new 勇气之霎(),
                    (long)PassiveID.深海重击 => new 深海重击(),
                    (long)PassiveID.反击螺旋 => new 反击螺旋(),
                    (long)PassiveID.刀光谍影 => new 刀光谍影(),
                    (long)PassiveID.幽冥剧毒 => new 幽冥剧毒(),
                    // 辅助型通用被动（2026-09-22 新增）：给予队友治疗 / 护盾 / 增益
                    (long)PassiveID.生命链接 => new 生命链接(),
                    (long)PassiveID.共鸣护盾 => new 共鸣护盾(),
                    (long)PassiveID.战地巡诊 => new 战地巡诊(),
                    (long)PassiveID.同袍之誓 => new 同袍之誓(),
                    (long)PassiveID.援护号令 => new 援护号令(),
                    (long)PassiveID.战术余裕 => new 战术余裕(),
                    (long)ItemPassiveID.万能钥匙 => new 万能钥匙技能(),
                    (long)ItemPassiveID.节拍器 => new 节拍器技能(),
                    (long)ItemPassiveID.攻击之爪 => new 攻击之爪技能(),
                    (long)ItemPassiveID.糖糖一周年纪念武器 => new 糖糖一周年纪念武器技能(),
                    (long)ItemPassiveID.糖糖一周年纪念防具 => new 糖糖一周年纪念防具技能(),
                    (long)ItemPassiveID.糖糖一周年纪念鞋子 => new 糖糖一周年纪念鞋子技能(),
                    (long)ItemPassiveID.糖糖一周年纪念饰品1 => new 糖糖一周年纪念饰品技能1(),
                    (long)ItemPassiveID.糖糖一周年纪念饰品2 => new 糖糖一周年纪念饰品技能2(),
                    (long)ItemActiveID.经验书 => new 经验书技能(),
                    (long)ItemActiveID.礼包 => new 礼包技能(),
                    (long)ItemActiveID.应急装置 => new 应急装置技能(),
                    _ => null
                };
            };
        }

        protected override Factory.EffectFactoryDelegate EffectFactory()
        {
            return (id, name, skill, args) =>
            {
                args ??= [];
                return (EffectID)id switch
                {
                    EffectID.ExATK => new ExATK(skill, args),
                    EffectID.ExDEF => new ExDEF(skill, args),
                    EffectID.ExSTR => new ExSTR(skill, args),
                    EffectID.ExAGI => new ExAGI(skill, args),
                    EffectID.ExINT => new ExINT(skill, args),
                    EffectID.SkillHardTimeReduce => new SkillHardTimeReduce(skill, args),
                    EffectID.NormalAttackHardTimeReduce => new NormalAttackHardTimeReduce(skill, args),
                    EffectID.AccelerationCoefficient => new AccelerationCoefficient(skill, args),
                    EffectID.ExSPD => new ExSPD(skill, args),
                    EffectID.ExActionCoefficient => new ExActionCoefficient(skill, args),
                    EffectID.ExCDR => new ExCDR(skill, args),
                    EffectID.ExMaxHP => new ExMaxHP(skill, args),
                    EffectID.ExMaxMP => new ExMaxMP(skill, args),
                    EffectID.ExCritRate => new ExCritRate(skill, args),
                    EffectID.ExCritDMG => new ExCritDMG(skill, args),
                    EffectID.ExEvadeRate => new ExEvadeRate(skill, args),
                    EffectID.PhysicalPenetration => new PhysicalPenetration(skill, args),
                    EffectID.MagicalPenetration => new MagicalPenetration(skill, args),
                    EffectID.ExPDR => new ExPDR(skill, args),
                    EffectID.ExMDF => new ExMDF(skill, args),
                    EffectID.ExHR => new ExHR(skill, args),
                    EffectID.ExMR => new ExMR(skill, args),
                    EffectID.ExATK2 => new ExATK2(skill, args),
                    EffectID.ExDEF2 => new ExDEF2(skill, args),
                    EffectID.ExSTR2 => new ExSTR2(skill, args),
                    EffectID.ExAGI2 => new ExAGI2(skill, args),
                    EffectID.ExINT2 => new ExINT2(skill, args),
                    EffectID.SkillHardTimeReduce2 => new SkillHardTimeReduce2(skill, args),
                    EffectID.NormalAttackHardTimeReduce2 => new NormalAttackHardTimeReduce2(skill, args),
                    EffectID.ExMaxHP2 => new ExMaxHP2(skill, args),
                    EffectID.ExMaxMP2 => new ExMaxMP2(skill, args),
                    EffectID.DynamicsEffect => new DynamicsEffect(skill, args),
                    EffectID.IgnoreEvade => new IgnoreEvade(skill, args),
                    EffectID.ExATR => new ExATR(skill, args),
                    EffectID.ExMOV => new ExMOV(skill, args),
                    EffectID.ExLifesteal => new ExLifesteal(skill, args),
                    EffectID.RecoverHP => new RecoverHP(skill, args),
                    EffectID.RecoverMP => new RecoverMP(skill, args),
                    EffectID.RecoverHP2 => new RecoverHP2(skill, args),
                    EffectID.RecoverMP2 => new RecoverMP2(skill, args),
                    EffectID.GetEP => new GetEP(skill, args),
                    EffectID.GetEXP => new GetEXP(skill, args),
                    _ => null
                };
            };
        }
    }
}
