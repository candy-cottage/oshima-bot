using OshimaBot.Game.Constants;
using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.PrefabricatedEntity;
using OshimaBot.Content.Characters;
using OshimaBot.Content.Effects.OpenEffects;
using OshimaBot.Content.Items;
using OshimaBot.Content.Skills;
using OshimaBot.Content.Others;

namespace OshimaBot.Game.Services
{
    public class FunGameService
    {
        public static List<Character> Characters { get; } = [];

        /// <summary>地区（旧版 <c>FunGameService.Regions</c>；v3 内容包从旧版 OshimaModules/Regions 复制而来）。</summary>
        public static List<Region> Regions { get; } = [];
        public static List<Skill> Skills { get; } = [];
        public static List<Skill> PassiveSkills { get; } = [];
        public static List<Skill> CommonPassiveSkills { get; } = [];
        public static List<Skill> SuperSkills { get; } = [];
        public static List<Skill> CommonSuperSkills { get; } = [];
        public static List<Skill> Magics { get; } = [];
        public static List<Item> Equipment { get; } = [];
        public static List<Item> Items { get; } = [];
        public static List<Skill> ItemSkills { get; } = [];
        public static List<Item> AllItems { get; } = [];
        public static List<Skill> AllSkills { get; } = [];

        public static void InitFunGame()
        {
            Characters.Add(new OshimaShiya());
            Characters.Add(new XinYin());
            Characters.Add(new Yang());
            Characters.Add(new NanGanYu());
            Characters.Add(new NiuNan());
            Characters.Add(new DokyoMayor());
            Characters.Add(new MagicalGirl());
            Characters.Add(new QingXiang());
            Characters.Add(new QWQAQW());
            Characters.Add(new ColdBlue());
            Characters.Add(new Dddovo());
            Characters.Add(new Quduoduo());
            Characters.Add(new ShiYu());
            Characters.Add(new XReouni());
            Characters.Add(new Neptune());
            Characters.Add(new CHAOS());
            Characters.Add(new Ryuko());
            Characters.Add(new TheGodK());

            Regions.AddRange(OshimaBot.Content.Regions.OshimaRegions.CreateAll());

            Skills.AddRange([new 疾风步(), new 助威(), new 挑拨(), new 绞丝棍(), new 金刚击(), new 旋风轮(), new 双连击(), new 绝影(), new 胧(), new 魔眼(),
                new 天堂之吻(), new 回复弹(), new 养命功(), new 镜花水月(), new 剑风闪(), new 鲨鱼锚击(), new 疾走(), new 闪现(),
                new 风之鞭(), new 拘束之鞭(), new 狐媚暗随(), new 快速狙击(), new 精准射击(), new 欢乐激发(), new 乱心安魂曲(), new 斗魂(), new 岚(), new 公牛之怒(),
                new 火焰碎击(), new 螺旋之刃(), new 霸王疾风(), new 导力装甲(), new 回复原状(), new 强打(), new 龙神功(), new 月华掌(), new 雷神脚(), new 弓刃交错(),
                new 神圣祈祷(), new 牺牲之箭(), new 石化之矢(), new 死亡制裁(), new 落叶(), new 风花阵(), new 陀螺舞(), new 光破斩(), new 跳跃点射(), new 光鬼斩(),
                new 雷索吸缚(), new 狂刃剑舞(), new 无相飞刀(), new 破邪显正(), new 号令(), new 千剑之雨(), new 无尽剑制(), new 灾难一掷(), new 血腥旋转(),
                new 抢夺命运(), new 命运剥夺(), new 命运馈赠(), new 命运之赐(), new 断罪斩(),
                // 决策点维度（2026-09-23 新增）
                new 战术部署(), new 过载(),
                // 决策点/节奏维度（2026-09-23 新增）
                new 战术余裕()]);

            SuperSkills.AddRange([new 极寒渴望(), new 身心一境(), new 绝对领域(), new 零式灭杀(), new 三相灵枢(), new 变幻之心(), new 熵灭极诣(), new 残香凋零(), new 饕餮盛宴(),
                new 宿命时律(), new 千羽瞬华(), new 咒怨洪流(), new 放监(), new 归元环(), new 海王星的野望(), new 全军出击(), new 宿命之潮(), new 神之因果()]);

            PassiveSkills.AddRange([new META马(), new 心灵之弦(), new 蚀魂震击(), new 灵能反射(), new 双生流转(), new 零式崩解(), new 少女绮想(), new 暗香疏影(), new 破釜沉舟(),
                new 累积之压(), new 银隼之赐(), new 弱者猎手(), new 开宫(), new 八卦阵(), new 深海之戟(), new 雇佣兵团(), new 不息之流(), new 概念之骰()]);

            CommonPassiveSkills.AddRange([new 征服者(), new 致命节奏(), new 强攻(), new 电刑(), new 黑暗收割(), new 迅捷步法(), new 贪欲猎手(),
                new 丛刃(), new 召唤艾黎(), new 相位猛冲(), new 奥术彗星(), new 风暴聚集(), new 不灭之握(), new 余震(), new 守护者(), new 骸骨镀层(), new 冰川增幅(),
                new 先攻(), new 饼干配送(), new 折射(), new 恩赐解脱(), new 静电场(), new 竭心光环(), new 海妖外壳(), new 自然蔽护(), new 勇气之霎(),
                new 深海重击(), new 反击螺旋(), new 刀光谍影(), new 幽冥剧毒(), new 掠夺者(),
                // 辅助型通用被动（2026-09-22 新增）：给予队友治疗 / 护盾 / 增益
                new 生命链接(), new 共鸣护盾(), new 战地巡诊(), new 同袍之誓(), new 援护号令()]);

            CommonSuperSkills.AddRange([new 樱花无双击(), new 漆黑之牙(), new 女王之怒(), new 裁决塔罗(), new 光明之环(), new 圣星光旋(), new 炎龙倒海(), new 卫星激光(), new 泰山玄武靠(),
                new 星杯领域(), new 魔枪洛亚(), new 八叶灭杀(), new 樱花残月(), new 圣洁祝福(), new 天堂阻灭(), new 歼灭(), new 催战鼓()]);

            Magics.AddRange([new 冰霜攻击(), new 火之矢(), new 水之矢(), new 风之轮(), new 石之锤(), new 心灵之霞(), new 次元上升(), new 暗物质(),
                new 回复术(), new 治愈术(), new 复苏术(), new 圣灵术(), new 时间加速(), new 时间减速(), new 反魔法领域(), new 沉默十字(), new 虚弱领域(), new 混沌烙印(), new 凝胶稠絮(),
                new 大地之墙(), new 盖亚之盾(), new 风之守护(), new 结晶防护(), new 强音之力(), new 神圣祝福(), new 根源屏障(), new 灾难冲击波(), new 银色荆棘(), new 等离子之波(),
                new 地狱之门(), new 钻石星尘(), new 死亡咆哮(), new 鬼魅之痛(), new 导力停止(), new 冰狱冥嚎(), new 火山咆哮(), new 水蓝轰炸(), new 岩石之息(), new 弧形日珥(), new 苍白地狱(), new 破碎虚空(),
                new 弧光消耗(), new 回复术改(), new 回复术复(), new 治愈术复(), new 风之守护复(), new 强音之力复(), new 结晶防护复(), new 神圣祝福复(), new 时间加速改(), new 时间减速改(),
                new 时间加速复(), new 时间减速复(), new 十二宫星环(), new 时间剥夺()]);

            Dictionary<string, Item> exItems = Factory.GetGameModuleInstances<Item>(OshimaGameModuleConstant.General, OshimaGameModuleConstant.Item);
            Equipment.AddRange(exItems.Values.Where(i => (int)i.ItemType >= 0 && (int)i.ItemType < 5));
            Equipment.AddRange([new 攻击之爪10(), new 攻击之爪25(), new 攻击之爪40(), new 攻击之爪55(), new 攻击之爪70(), new 攻击之爪85(), new 糖糖一周年纪念武器(),
                new 糖糖一周年纪念防具(), new 糖糖一周年纪念鞋子(), new 糖糖一周年纪念饰品1(), new 糖糖一周年纪念饰品2(),
                new 行军医箱1(), new 行军医箱2(), new 行军医箱3(),
                new 共鸣核心1(), new 共鸣核心2(), new 共鸣核心3(),
                new 接力信标1(), new 接力信标2(), new 接力信标3(),
                new 应急装置1(), new 应急装置2(), new 应急装置3(),
                // 决策点/节奏维度（2026-09-23 新增）
                new 万能钥匙1(), new 万能钥匙2(), new 万能钥匙3(),
                new 节拍器1(), new 节拍器2(), new 节拍器3()]);

            Items.AddRange(exItems.Values.Where(i => (int)i.ItemType > 4));
            Items.AddRange([new 小经验书(), new 中经验书(), new 大经验书(), new 升华之印(), new 流光之印(), new 永恒之印(), new 技能卷轴(), new 智慧之果(), new 奥术符文(), new 混沌之核(),
                new 小回复药(), new 中回复药(), new 大回复药(), new 魔力填充剂1(), new 魔力填充剂2(), new 魔力填充剂3(), new 能量饮料1(), new 能量饮料2(), new 能量饮料3(), new 年夜饭(), new 蛇年大吉(), new 新春快乐(), new 毕业礼包(),
                new 复苏药1(), new 复苏药2(), new 复苏药3(), new 全回复药(), new 魔法卡礼包(), new 奖券(), new 十连奖券(), new 改名卡(), new 原初之印(), new 创生之印(), new 法则精粹(), new 大师锻造券(),
                new 一周年纪念礼包(), new 一周年纪念套装(), new 冬至快乐(), new 圣诞礼包(), new 元旦快乐()
            ]);

            AllItems.AddRange(Equipment);
            AllItems.AddRange(Items);

            Skill?[] activeSkills = [.. Equipment.Select(i => i.Skills.Active), .. Items.Select(i => i.Skills.Active)];
            foreach (Skill? skill in activeSkills)
            {
                if (skill != null)
                {
                    ItemSkills.Add(skill);
                }
            }
            ItemSkills.AddRange([.. Equipment.SelectMany(i => i.Skills.Passives), .. Items.SelectMany(i => i.Skills.Passives)]);

            AllSkills.AddRange(Magics);
            AllSkills.AddRange(Skills);
            AllSkills.AddRange(PassiveSkills);
            AllSkills.AddRange(CommonPassiveSkills);
            AllSkills.AddRange(ItemSkills);
            AllSkills.AddRange(SuperSkills);
            AllSkills.AddRange(CommonSuperSkills);
        }

        public static void Reload()
        {
            Characters.Clear();
            Equipment.Clear();
            Skills.Clear();
            SuperSkills.Clear();
            CommonSuperSkills.Clear();
            PassiveSkills.Clear();
            CommonPassiveSkills.Clear();
            Magics.Clear();
            AllItems.Clear();
            ItemSkills.Clear();
            AllSkills.Clear();
            InitFunGame();
        }

        public static void AddCharacterSkills(Character character, int passiveLevel, int skillLevel, int superLevel)
        {
            long id = character.Id;
            Math.Sign(skillLevel);
            if (id == 1)
            {
                Skill META马 = new META马(character)
                {
                    Level = passiveLevel
                };
                character.Skills.Add(META马);

                Skill 熵灭极诣 = new 熵灭极诣(character)
                {
                    Level = superLevel
                };
                character.Skills.Add(熵灭极诣);
            }

            if (id == 2)
            {
                Skill 心灵之弦 = new 心灵之弦(character)
                {
                    Level = passiveLevel
                };
                character.Skills.Add(心灵之弦);

                Skill 千羽瞬华 = new 千羽瞬华(character)
                {
                    Level = superLevel
                };
                character.Skills.Add(千羽瞬华);
            }

            if (id == 3)
            {
                Skill 蚀魂震击 = new 蚀魂震击(character)
                {
                    Level = passiveLevel
                };
                character.Skills.Add(蚀魂震击);

                Skill 咒怨洪流 = new 咒怨洪流(character)
                {
                    Level = superLevel
                };
                character.Skills.Add(咒怨洪流);
            }

            if (id == 4)
            {
                Skill 灵能反射 = new 灵能反射(character)
                {
                    Level = passiveLevel
                };
                character.Skills.Add(灵能反射);

                Skill 三相灵枢 = new 三相灵枢(character)
                {
                    Level = superLevel
                };
                character.Skills.Add(三相灵枢);
            }

            if (id == 5)
            {
                Skill 双生流转 = new 双生流转(character)
                {
                    Level = passiveLevel
                };
                character.Skills.Add(双生流转);

                Skill 变幻之心 = new 变幻之心(character)
                {
                    Level = superLevel
                };
                character.Skills.Add(变幻之心);
            }

            if (id == 6)
            {
                Skill 零式崩解 = new 零式崩解(character)
                {
                    Level = passiveLevel
                };
                character.Skills.Add(零式崩解);

                Skill 零式灭杀 = new 零式灭杀(character)
                {
                    Level = superLevel
                };
                character.Skills.Add(零式灭杀);
            }

            if (id == 7)
            {
                Skill 少女绮想 = new 少女绮想(character)
                {
                    Level = passiveLevel
                };
                character.Skills.Add(少女绮想);

                Skill 绝对领域 = new 绝对领域(character)
                {
                    Level = superLevel
                };
                character.Skills.Add(绝对领域);
            }

            if (id == 8)
            {
                Skill 暗香疏影 = new 暗香疏影(character)
                {
                    Level = passiveLevel
                };
                character.Skills.Add(暗香疏影);

                Skill 残香凋零 = new 残香凋零(character)
                {
                    Level = superLevel
                };
                character.Skills.Add(残香凋零);
            }

            if (id == 9)
            {
                Skill 破釜沉舟 = new 破釜沉舟(character)
                {
                    Level = passiveLevel
                };
                character.Skills.Add(破釜沉舟);

                Skill 宿命时律 = new 宿命时律(character)
                {
                    Level = superLevel
                };
                character.Skills.Add(宿命时律);
            }

            if (id == 10)
            {
                Skill 累积之压 = new 累积之压(character)
                {
                    Level = passiveLevel
                };
                character.Skills.Add(累积之压);

                Skill 极寒渴望 = new 极寒渴望(character)
                {
                    Level = superLevel
                };
                character.Skills.Add(极寒渴望);
            }

            if (id == 11)
            {
                Skill 银隼之赐 = new 银隼之赐(character)
                {
                    Level = passiveLevel
                };
                character.Skills.Add(银隼之赐);

                Skill 身心一境 = new 身心一境(character)
                {
                    Level = superLevel
                };
                character.Skills.Add(身心一境);
            }

            if (id == 12)
            {
                Skill 弱者猎手 = new 弱者猎手(character)
                {
                    Level = passiveLevel
                };
                character.Skills.Add(弱者猎手);

                Skill 饕餮盛宴 = new 饕餮盛宴(character)
                {
                    Level = superLevel
                };
                character.Skills.Add(饕餮盛宴);
            }

            if (id == 13)
            {
                Skill 开宫 = new 开宫(character)
                {
                    Level = passiveLevel
                };
                character.Skills.Add(开宫);

                Skill 放监 = new 放监(character)
                {
                    Level = superLevel
                };
                character.Skills.Add(放监);
            }

            if (id == 14)
            {
                Skill 八卦阵 = new 八卦阵(character)
                {
                    Level = passiveLevel
                };
                character.Skills.Add(八卦阵);

                Skill 归元环 = new 归元环(character)
                {
                    Level = superLevel
                };
                character.Skills.Add(归元环);
            }

            if (id == 15)
            {
                Skill 深海之戟 = new 深海之戟(character)
                {
                    Level = passiveLevel
                };
                character.Skills.Add(深海之戟);

                Skill 海王星的野望 = new 海王星的野望(character)
                {
                    Level = superLevel
                };
                character.Skills.Add(海王星的野望);
            }

            if (id == 16)
            {
                Skill 雇佣兵团 = new 雇佣兵团(character)
                {
                    Level = passiveLevel
                };
                character.Skills.Add(雇佣兵团);

                Skill 全军出击 = new 全军出击(character)
                {
                    Level = superLevel
                };
                character.Skills.Add(全军出击);
            }

            if (id == 17)
            {
                Skill 不息之流 = new 不息之流(character)
                {
                    Level = passiveLevel
                };
                character.Skills.Add(不息之流);

                Skill 宿命之潮 = new 宿命之潮(character)
                {
                    Level = superLevel
                };
                character.Skills.Add(宿命之潮);
            }

            if (id == 18)
            {
                Skill 概念之骰 = new 概念之骰(character)
                {
                    Level = passiveLevel
                };
                character.Skills.Add(概念之骰);

                Skill 神之因果 = new 神之因果(character)
                {
                    Level = superLevel
                };
                character.Skills.Add(神之因果);
            }
        }

        public static List<Item> GenerateMagicCards(Random random, int count, QualityType? qualityType = null, long[]? magicIds = null, (int str, int agi, int intelligence)[]? values = null)
        {
            List<Item> items = [];

            for (int i = 0; i < count; i++)
            {
                long magicId = 0;
                if (magicIds != null && magicIds.Length > i) magicId = magicIds[i];
                (int str, int agi, int intelligence) = (0, 0, 0);
                if (values != null && values.Length > i)
                {
                    str = values[i].str;
                    agi = values[i].agi;
                    intelligence = values[i].intelligence;
                }
                items.Add(GenerateMagicCard(random, qualityType, magicId, str, agi, intelligence));
            }

            return items;
        }

        public static Item GenerateMagicCard(Random random, QualityType? qualityType = null, long magicId = 0, int str = 0, int agi = 0, int intelligence = 0)
        {
            Item item = new()
            {
                Id = CreateItemId("16", random),
                Name = RandomNames.GenerateRandomChineseName(random),
                ItemType = ItemType.MagicCard,
                RemainUseTimes = 1
            };

            GenerateAndAddSkillToMagicCard(random, item, qualityType, magicId, str, agi, intelligence);

            return item;
        }

        public static void GenerateAndAddSkillToMagicCard(Random random, Item item, QualityType? qualityType = null, long magicId = 0, int str = 0, int agi = 0, int intelligence = 0)
        {
            int total = str + agi + intelligence;
            if (total == 0)
            {
                if (qualityType != null)
                {
                    item.QualityType = qualityType.Value;
                    if (item.QualityType > QualityType.Gold) item.QualityType = QualityType.Gold;
                    total = item.QualityType switch
                    {
                        QualityType.Green => random.Next(7, 13),
                        QualityType.Blue => random.Next(13, 19),
                        QualityType.Purple => random.Next(19, 25),
                        QualityType.Orange => random.Next(25, 31),
                        QualityType.Red => random.Next(31, 37),
                        QualityType.Gold => random.Next(37, 43),
                        _ => random.Next(1, 7)
                    };
                }
                else total = random.Next(1, 43);

                // 随机决定将多少个属性赋给其中一个属性，确保至少一个不为零
                int nonZeroAttributes = random.Next(1, Math.Min(4, total + 1)); // 随机决定非零属性的数量，确保在 total = 1 时最多只有1个非零属性

                // 根据非零属性数量分配属性点
                if (nonZeroAttributes == 1)
                {
                    // 只有一个属性不为零
                    int attribute = random.Next(0, 3);
                    if (attribute == 0) str = total;
                    else if (attribute == 1) agi = total;
                    else intelligence = total;
                }
                else if (nonZeroAttributes == 2 && total >= 2)
                {
                    // 两个属性不为零
                    int first = random.Next(1, total); // 第一个属性的值
                    int second = total - first; // 第二个属性的值

                    int attribute = random.Next(0, 3);
                    if (attribute == 0)
                    {
                        str = first;
                    }
                    else if (attribute == 1)
                    {
                        agi = first;
                    }
                    else
                    {
                        intelligence = first;
                    }

                    attribute = random.Next(0, 3);
                    while ((attribute == 0 && str > 0) || (attribute == 1 && agi > 0) || (attribute == 2 && intelligence > 0))
                    {
                        attribute = random.Next(0, 3);
                    }

                    if (attribute == 0)
                    {
                        str = second;
                    }
                    else if (attribute == 1)
                    {
                        agi = second;
                    }
                    else
                    {
                        intelligence = second;
                    }
                }
                else if (total >= 3)
                {
                    // 三个属性都不为零
                    str = random.Next(1, total - 1); // 第一个属性的值
                    agi = random.Next(1, total - str); // 第二个属性的值
                    intelligence = total - str - agi; // 剩下的值给第三个属性
                }
            }

            if (item.QualityType == QualityType.White)
            {
                if (total > 6 && total <= 12)
                {
                    item.QualityType = QualityType.Green;
                }
                else if (total > 12 && total <= 18)
                {
                    item.QualityType = QualityType.Blue;
                }
                else if (total > 18 && total <= 24)
                {
                    item.QualityType = QualityType.Purple;
                }
                else if (total > 24 && total <= 30)
                {
                    item.QualityType = QualityType.Orange;
                }
                else if (total > 30 && total <= 36)
                {
                    item.QualityType = QualityType.Red;
                }
                else if (total > 36)
                {
                    item.QualityType = QualityType.Gold;
                }
            }

            Skill? magic = null;
            if (magicId != 0)
            {
                magic = Magics.FirstOrDefault(m => m.Id == magicId);
            }
            magic ??= Magics[random.Next(Magics.Count)].Copy();
            magic.AssociatedItemGuid = item.Guid;
            magic.Level = (int)item.QualityType switch
            {
                2 => 2,
                3 => 2,
                4 => 3,
                5 => 4,
                6 => 5,
                _ => 1
            };
            if (magic.Level > 1)
            {
                item.Name += $" +{magic.Level - 1}";
            }
            item.Skills.Active = magic;

            Skill skill = Factory.OpenFactory.GetInstance<Skill>(item.Id, "动态矩阵", []);
            GenerateAndAddEffectsToMagicCard(skill, str, agi, intelligence);

            skill.Level = 1;
            List<string> strings = [];
            if (str > 0) strings.Add($"{str:0.##} 点力量");
            if (agi > 0) strings.Add($"{agi:0.##} 点敏捷");
            if (intelligence > 0) strings.Add($"{intelligence:0.##} 点智力");
            item.Description = $"包含魔法：{item.Skills.Active.Name + (item.Skills.Active.Level > 1 ? $" +{item.Skills.Active.Level - 1}" : "")}\r\n" +
                $"增加角色属性：{string.Join("，", strings)}";
            item.Skills.Passives.Add(skill);
        }

        public static void GenerateAndAddEffectsToMagicCard(Skill skill, int str, int agi, int intelligence)
        {
            if (str > 0)
            {
                skill.Effects.Add(Factory.OpenFactory.GetInstance((long)EffectID.ExSTR, "", skill, new()
                {
                    { "exstr", str }
                }));
            }

            if (agi > 0)
            {
                skill.Effects.Add(Factory.OpenFactory.GetInstance((long)EffectID.ExAGI, "", skill, new()
                {
                    { "exagi", agi }
                }));
            }

            if (intelligence > 0)
            {
                skill.Effects.Add(Factory.OpenFactory.GetInstance((long)EffectID.ExINT, "", skill, new()
                {
                    { "exint", intelligence }
                }));
            }
        }

        public static Item? ConflateMagicCardPack(Random random, IEnumerable<Item> magicCards)
        {
            if (magicCards.Any())
            {
                List<Skill> magics = [.. magicCards.Where(i => i.Skills.Active != null).Select(i => i.Skills.Active!)];
                List<Skill> passives = [.. magicCards.SelectMany(i => i.Skills.Passives)];
                Item item = new()
                {
                    Id = CreateItemId("10", random),
                    Name = RandomNames.GenerateRandomChineseName(random),
                    ItemType = ItemType.MagicCardPack
                };
                double str = 0, agi = 0, intelligence = 0;
                foreach (Skill skill in passives)
                {
                    Skill newSkill = skill.Copy();
                    foreach (Effect effect in newSkill.Effects)
                    {
                        switch ((EffectID)effect.Id)
                        {
                            case EffectID.ExSTR:
                                if (effect is ExSTR exstr)
                                {
                                    str += exstr.Value;
                                }
                                break;
                            case EffectID.ExAGI:
                                if (effect is ExAGI exagi)
                                {
                                    agi += exagi.Value;
                                }
                                break;
                            case EffectID.ExINT:
                                if (effect is ExINT exint)
                                {
                                    intelligence += exint.Value;
                                }
                                break;
                        }
                    }
                    newSkill.Level = skill.Level;
                    newSkill.Item = item;
                    item.Skills.Passives.Add(newSkill);
                }
                List<string> strings = [];
                if (str > 0) strings.Add($"{str:0.##} 点力量");
                if (agi > 0) strings.Add($"{agi:0.##} 点敏捷");
                if (intelligence > 0) strings.Add($"{intelligence:0.##} 点智力");
                foreach (Skill skill in magics)
                {
                    IEnumerable<Skill> has = item.Skills.Magics.Where(m => m.Id == skill.Id);
                    if (has.Any() && has.First() is Skill s)
                    {
                        s.Level += skill.Level;
                        if (s.Level > 1) s.Name = s.Name.Split(' ')[0] + $" +{s.Level - 1}";
                    }
                    else
                    {
                        Skill magic = skill.Copy();
                        magic.AssociatedItemGuid = item.Guid;
                        magic.Level = skill.Level;
                        item.Skills.Magics.Add(magic);
                    }
                }
                item.Description = $"包含魔法：{string.Join("，", item.Skills.Magics.Select(m => m.Name + (m.Level > 1 ? $" +{m.Level - 1}" : "")))}\r\n" +
                    $"增加角色属性：{string.Join("，", strings)}";
                double total = str + agi + intelligence;
                if (total > 18 && total <= 36)
                {
                    item.QualityType = QualityType.Green;
                }
                else if (total > 36 && total <= 54)
                {
                    item.QualityType = QualityType.Blue;
                }
                else if (total > 54 && total <= 72)
                {
                    item.QualityType = QualityType.Purple;
                }
                else if (total > 72 && total <= 90)
                {
                    item.QualityType = QualityType.Orange;
                }
                else if (total > 90 && total <= 108)
                {
                    item.QualityType = QualityType.Red;
                }
                else if (total > 108)
                {
                    item.QualityType = QualityType.Gold;
                }
                return item;
            }
            return null;
        }

        public static Item? GenerateMagicCardPack(Random random, int magicCardCount, QualityType? qualityType = null, long[]? magicIds = null, (int str, int agi, int intelligence)[]? values = null)
        {
            List<Item> magicCards = GenerateMagicCards(random, magicCardCount, qualityType, magicIds, values);
            Item? magicCardPack = ConflateMagicCardPack(random, magicCards);
            return magicCardPack;
        }

        /// <summary>
        /// 以核心库 MagicCardPack 为载体的卡包（新做法）：三围写入原生 AttributeBoosts（exstr/exagi/exint），
        /// 不再生成 Ex 被动特效技能。魔法合并 / 品质映射 / 描述口径与 ConflateMagicCardPack 保持一致。
        /// </summary>
        public static Item? GenerateCoreMagicCardPack(Random random, int magicCardCount, QualityType? qualityType = null, long[]? magicIds = null, (int str, int agi, int intelligence)[]? values = null)
        {
            List<Item> magicCards = GenerateMagicCards(random, magicCardCount, qualityType, magicIds, values);
            return ConflateCoreMagicCardPack(random, magicCards);
        }

        /// <summary>
        /// 将若干魔法卡合成为核心库 MagicCardPack。
        /// 说明：不修改原 ConflateMagicCardPack / 卡片生成流程；三围取自卡片被动技能的 Ex 特效（typed 优先、参数 Values 兜底）。
        /// 宿主未注册特效工厂时 OpenFactory 只会产出 Id=0 且参数丢失的泛型 Effect——新方法与旧方法在该类宿主下的取值表现一致。
        /// </summary>
        public static Item? ConflateCoreMagicCardPack(Random random, IEnumerable<Item> magicCards)
        {
            if (!magicCards.Any())
            {
                return null;
            }
            List<Skill> magics = [.. magicCards.Where(i => i.Skills.Active != null).Select(i => i.Skills.Active!)];
            double str = 0, agi = 0, intelligence = 0;
            foreach (Item card in magicCards)
            {
                foreach (Skill skill in card.Skills.Passives)
                {
                    foreach (Effect effect in skill.Effects)
                    {
                        double ex = 0, ag = 0, it = 0;
                        switch (effect)
                        {
                            case ExSTR s: ex = s.Value; break;
                            case ExAGI a: ag = a.Value; break;
                            case ExINT i: it = i.Value; break;
                            default:
                                foreach (KeyValuePair<string, object> kv in effect.Values)
                                {
                                    if (kv.Key.Equals("exstr", StringComparison.OrdinalIgnoreCase) && double.TryParse(kv.Value.ToString(), out double sx)) ex = sx;
                                    else if (kv.Key.Equals("exagi", StringComparison.OrdinalIgnoreCase) && double.TryParse(kv.Value.ToString(), out double ax)) ag = ax;
                                    else if (kv.Key.Equals("exint", StringComparison.OrdinalIgnoreCase) && double.TryParse(kv.Value.ToString(), out double ix)) it = ix;
                                }
                                break;
                        }
                        str += ex;
                        agi += ag;
                        intelligence += it;
                    }
                }
            }
            MagicCardPack item = new(new Dictionary<string, object>
            {
                { "exstr", str },
                { "exagi", agi },
                { "exint", intelligence }
            })
            {
                Id = CreateItemId("10", random),
                Name = RandomNames.GenerateRandomChineseName(random)
            };
            List<string> strings = [];
            if (str > 0) strings.Add($"{str:0.##} 点力量");
            if (agi > 0) strings.Add($"{agi:0.##} 点敏捷");
            if (intelligence > 0) strings.Add($"{intelligence:0.##} 点智力");
            foreach (Skill skill in magics)
            {
                IEnumerable<Skill> has = item.Skills.Magics.Where(m => m.Id == skill.Id);
                if (has.Any() && has.First() is Skill s)
                {
                    s.Level += skill.Level;
                    if (s.Level > 1) s.Name = s.Name.Split(' ')[0] + $" +{s.Level - 1}";
                }
                else
                {
                    Skill magic = skill.Copy();
                    magic.AssociatedItemGuid = item.Guid;
                    magic.Level = skill.Level;
                    item.Skills.Magics.Add(magic);
                }
            }
            item.Description = $"包含魔法：{string.Join("，", item.Skills.Magics.Select(m => m.Name + (m.Level > 1 ? $" +{m.Level - 1}" : "")))}\r\n" +
                $"增加角色属性：{string.Join("，", strings)}";
            double total = str + agi + intelligence;
            if (total > 18 && total <= 36)
            {
                item.QualityType = QualityType.Green;
            }
            else if (total > 36 && total <= 54)
            {
                item.QualityType = QualityType.Blue;
            }
            else if (total > 54 && total <= 72)
            {
                item.QualityType = QualityType.Purple;
            }
            else if (total > 72 && total <= 90)
            {
                item.QualityType = QualityType.Orange;
            }
            else if (total > 90 && total <= 108)
            {
                item.QualityType = QualityType.Red;
            }
            else if (total > 108)
            {
                item.QualityType = QualityType.Gold;
            }
            return item;
        }

        public static double CalculateRating(CharacterStatistics stats, Team? team = null, CharacterStatistics[]? allStats = null)
        {
            double k = stats.Kills;
            double a = stats.Assists;
            double d = Math.Max(0, stats.Deaths);
            double dmg = stats.TotalDamage + (stats.TotalTrueDamage * 0.2);
            double heal = stats.TotalHeal + stats.TotalShield;
            double cc = stats.ControlTime;
            double taken = stats.TotalTakenDamage;
            double live = stats.LiveTime;

            if (team != null)
            {
                double teamTotalDmg = allStats?.Sum(s => s.TotalDamage + s.TotalTrueDamage * 0.2) ?? dmg;
                double teamTotalHeal = allStats?.Sum(s => s.TotalHeal + s.TotalShield) ?? heal;
                int playerCount = allStats?.Length ?? 1;

                double dmgShare = dmg / Math.Max(2.3, teamTotalDmg);
                double healShare = heal / Math.Max(1, teamTotalHeal);
                double roleContribution = Math.Max(dmgShare, healShare) * playerCount * 0.6;
                double roleScore = Math.Min(1.0, roleContribution);

                double kdaRatio = (k * 1.4 + a * 0.2) / (d + 1.8);
                double kdaScore = Math.Min(1.0, (kdaRatio / 3.0) * 0.4);

                double ccScore = Math.Min(0.10, (cc / 60.0) * 0.05);
                double tankScore = Math.Min(0.10, (taken / (d + 1) / 10000.0) * 0.1);

                double totalRating = roleScore + kdaScore + ccScore + tankScore;

                double avgDeaths = allStats?.Average(s => s.Deaths) ?? d;
                if (d > avgDeaths && kdaRatio < 1.0) totalRating *= 0.75;

                return Math.Round(Math.Max(0.01, totalRating), 4);
            }
            else
            {
                int rank = stats.LastRank;
                int totalPlayers = allStats?.Length ?? 10;
                double maxKills = allStats?.Max(s => s.Kills) ?? k;
                double maxDmg = allStats?.Max(s => s.TotalDamage + s.TotalTrueDamage * 0.2) ?? dmg;

                double rankScore = ((totalPlayers - rank + 1.0) / totalPlayers) * 0.8;

                double killPart = (k * 1.7 + a * 0.1) / Math.Max(1, maxKills + 1);
                double dmgPart = (dmg / Math.Max(1, maxDmg * 1.8)) * 0.1;
                double combatScore = Math.Min(0.8, killPart * 0.4 + dmgPart);

                double utilityScore = Math.Min(0.2, (cc / 60.0) * 0.04 + (heal / Math.Max(1, maxDmg)) * 0.05);

                double totalRating = rankScore + combatScore + utilityScore;

                if (k == 0)
                {
                    totalRating *= 0.6;
                }

                if (rank == 1 && k > 0)
                {
                    if (k >= maxKills) totalRating += 0.15;
                }

                if (rank > 5 && k >= maxKills * 0.8 && k > 0)
                {
                    totalRating += 0.15;
                }

                return Math.Round(Math.Max(0.01, totalRating), 4);
            }
        }

        public static void GetCharacterRating(Dictionary<Character, CharacterStatistics> statistics, bool isTeam, List<Team> teams)
        {
            foreach (Character character in statistics.Keys)
            {
                Team? team = null;
                CharacterStatistics[]? teammateStats = null;
                if (isTeam)
                {
                    team = teams.FirstOrDefault(t => t.IsOnThisTeam(character));
                    if (team != null)
                    {
                        teammateStats = [.. statistics.Where(kv => team.Members.Contains(kv.Key)).Select(kv => kv.Value)];
                    }
                }
                statistics[character].Rating = CalculateRating(statistics[character], team, teammateStats);
            }
        }

        /// <summary>
        /// 用本局随机源生成物品 Id（原 <see cref="Verification.CreateVerifyCode"/> 依赖 DateTime.Now + new Random()，不可复现）
        /// </summary>
        /// <param name="prefix">Id 前缀（"16"=魔法卡，"10"=卡包）</param>
        /// <param name="random">本局随机源</param>
        public static long CreateItemId(string prefix, Random random)
        {
            return Convert.ToInt64(prefix + random.Next(10_000_000, 100_000_000).ToString());
        }
    }
}