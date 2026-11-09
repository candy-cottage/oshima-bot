using OshimaBot.Game.Modules;

namespace OshimaBot.Game.Services
{
    /// <summary>
    /// 进程级游戏目录初始化。FunGame.Core 的工厂是全局状态，必须且只能初始化一次。
    /// </summary>
    public static class FunGameRuntime
    {
        private static readonly Lock Sync = new();
        private static bool _initialized;

        public static DateTimeOffset StartedAt { get; private set; }

        public static void EnsureInitialized()
        {
            lock (Sync)
            {
                if (_initialized) return;

                new CharacterModule().Load();
                new SkillModule().Load();
                new ItemModule().Load();
                FunGameService.InitFunGame();
                FunGameSimulation.InitializeStatistics();

                StartedAt = DateTimeOffset.UtcNow;
                _initialized = true;
            }
        }
    }
}
