namespace OshimaBot.Game.Services
{
    /// <summary>
    /// <see cref="FunGameSimulation.StartSimulationGame"/> 的参数包，替代原先一长串位置 / 命名参数
    /// <para/>· 用法：<c>new SimulationOptions { IsTeam = true, BindToCharacter = true }</c>
    /// <para/>· <see cref="MaxRespawnTimesMix"/> 在 <c>new</c> 时默认为 1（与旧版可选参数默认值一致），
    /// <see cref="BindToCharacter"/> 默认为 <c>true</c>（角色绑定的回合奖励默认启用），其余字段默认为 false
    /// </summary>
    public struct SimulationOptions
    {
        /// <summary>控制台输出开关（旧参数 printout）</summary>
        public bool PrintOut { get; set; }

        /// <summary>Web 模式：影响回合消息的收集与输出</summary>
        public bool IsWeb { get; set; }

        /// <summary>团队模式（TeamGamingQueue），否则为混战模式（MixGamingQueue）</summary>
        public bool IsTeam { get; set; }

        /// <summary>死斗模式输出逐回合详情</summary>
        public bool DeathMatchRoundDetail { get; set; }

        /// <summary>混战模式的最大复活次数</summary>
        public int MaxRespawnTimesMix { get; set; }

        /// <summary>启用商店（仅团队模式生效）</summary>
        public bool UseStore { get; set; }

        /// <summary>是否加载地图</summary>
        public bool HasMap { get; set; }

        /// <summary>调试模式（输出更多运行细节）</summary>
        public bool IsDebug { get; set; }

        /// <summary>
        /// S9：是否把回合记录写入 rounds_archive.zip。默认 false —— 归档改由
        /// simulation_runs 表承担，只有显式开启（复盘/平衡测试）才落盘。
        /// </summary>
        public bool ArchiveRounds { get; set; }

        /// <summary>
        /// 是否启用「角色绑定的回合奖励」（对应 <c>InitRoundRewards</c> 的 bindToCharacter）。<para/>
        /// <b>默认 true</b>：不启用时【命运XX】系技能（抢夺 / 剥夺 / 馈赠）与【强运】会全部静默失效。
        /// </summary>
        public bool BindToCharacter { get; set; }

        /// <summary>
        /// 初始角色等级（默认 10）。局内会由空投 <c>DropItems</c> 每 40 时间单位 +8 逐步提升，
        /// 直到 <c>GameplayEquilibriumConstant.MaxLevel</c>（60）。<para/>
        /// 平衡测试可设为 60 <b>直接拉满</b>，使实测口径与《数值设计手册·判定基准》的 Lv60 标尺一致
        /// —— 否则一局里前 ~60% 时长在低等级，会把实测值稀释（实测普攻每目标伤害在 t≈280 前只有满级的 1/5）。
        /// </summary>
        public int CharacterLevel { get; set; }

        /// <summary>
        /// 初始战技 / 魔法 / 爆发技等级（默认 2）。空投每次 +1，上限：战技·爆发技 6、魔法 8。
        /// 拉满用 6（战技·爆发技封顶）或 8（魔法封顶）。
        /// </summary>
        public int SkillLevel { get; set; }

        /// <summary>
        /// 初始普攻等级（默认 2）。空投每次 +1，上限 <c>MaxNormalAttackLevel</c>（8）。
        /// </summary>
        public int NormalAttackLevel { get; set; }

        /// <summary>
        /// 周期空投开关（默认 <c>true</c>，即保持原有对局行为）。<para/>
        /// · <c>true</c>：每 <b>40 时间单位</b>空投一次，发装备 + 魔法卡包并提升等级 —— 对局持续成长（实战态）。<br/>
        /// · <c>false</c>：只在开局发放一次（<c>DropItems(..., addLevel: false)</c>），此后装备与技能池保持恒定 —— <b>定态</b>。
        /// <para/>
        /// 定态用于测量「技能在固定装备/固定技能池下的收益」：可消除局内数值漂移带来的异方差
        /// （实测渐进态下普攻每目标伤害从 149 涨到 1136，同一技能的样本跨 7.6 倍量级）。
        /// 注意：开局那次空投不能省，否则角色没有装备、也没有魔法卡包附带的魔法。
        /// </summary>
        public bool EnablePeriodicDrop { get; set; }

        /// <summary>
        /// 固定武器 Id（默认 <c>0</c> = 不固定，按品质随机抽取）。<para/>
        /// 40 件武器**全部**带攻击力副属性，且 <c>exatk</c> 跨度 20（品质0）– 170（品质5）= <b>8.5×</b>，
        /// 副属性还各不相同（<c>excr</c>/<c>exppt</c>/<c>exmpt</c>/<c>excrd</c>/<c>exspd</c>/<c>exls</c>/<c>exatk2</c>），
        /// 武器类型倍率另有 0.85–1.20 的 1.41× 跨度。固定武器可一次性消除这些变量
        /// （防具/鞋/饰品中仅 2 件带攻击力，占比 2/107，影响可忽略）。<para/>
        /// 平衡测试建议配合 <see cref="EnablePeriodicDrop"/>=false 使用，
        /// 推荐值 <c>11575</c>（羔羊颂·品质5 单手剑，<c>exatk 110 + excr 0.3</c>，单手剑倍率 1.0 中性）。
        /// </summary>
        public long FixedWeaponId { get; set; }

        /// <summary>
        /// 构造参数包：<see cref="MaxRespawnTimesMix"/> 默认 1，等级默认为 10 / 2 / 2（与旧版硬编码一致），
        /// <see cref="EnablePeriodicDrop"/> 与 <see cref="BindToCharacter"/> 默认 true，其余布尔默认 false
        /// </summary>
        public SimulationOptions()
        {
            MaxRespawnTimesMix = 1;
            CharacterLevel = 10;
            SkillLevel = 2;
            NormalAttackLevel = 2;
            EnablePeriodicDrop = true;
            // 角色绑定的回合奖励默认启用：否则命运系技能与强运全部静默失效
            BindToCharacter = true;
        }
    }
}
