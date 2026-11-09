using OshimaBot.Game.Constants;
using FunGame.Core.Api;
using FunGame.Core.Entity;
using OshimaBot.Content.Characters;
using CM = FunGame.Core.Library.Module.CharacterModule;

namespace OshimaBot.Game.Modules
{
    public class CharacterModule : CM
    {
        public override string Name => OshimaGameModuleConstant.Character;
        public override string Description => OshimaGameModuleConstant.Description;
        public override string Version => OshimaGameModuleConstant.Version;
        public override string Author => OshimaGameModuleConstant.Author;
        public Dictionary<string, Character> KnownCharacters { get; } = [];

        public override Dictionary<string, Character> Characters
        {
            get
            {
                Dictionary<string, Character> characters = Factory.GetGameModuleInstances<Character>(OshimaGameModuleConstant.General, OshimaGameModuleConstant.Character);
                if (KnownCharacters.Count == 0 && characters.Count > 0)
                {
                    foreach (string key in characters.Keys)
                    {
                        KnownCharacters[key] = characters[key];
                    }
                }
                return characters;
            }
        }

        protected override Factory.EntityFactoryDelegate<Character> CharacterFactory()
        {
            return (id, name, args) =>
            {
                return id switch
                {
                    1 => new OshimaShiya(),
                    2 => new XinYin(),
                    3 => new Yang(),
                    4 => new NanGanYu(),
                    5 => new NiuNan(),
                    6 => new DokyoMayor(),
                    7 => new MagicalGirl(),
                    8 => new QingXiang(),
                    9 => new QWQAQW(),
                    10 => new ColdBlue(),
                    11 => new Dddovo(),
                    12 => new Quduoduo(),
                    13 => new ShiYu(),
                    14 => new XReouni(),
                    15 => new Neptune(),
                    16 => new CHAOS(),
                    17 => new Ryuko(),
                    18 => new TheGodK(),
                    _ => null,
                };
            };
        }
    }
}
