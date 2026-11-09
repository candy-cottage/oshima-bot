using System.Text;
using OshimaBot.Game.Services;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;

namespace OshimaBot.Services
{
    public sealed record CatalogEntry(long Id, string Name, string Category);

    public sealed class FunGameQueryService(FunGameSimulationService simulations)
    {
        public IReadOnlyList<CatalogEntry> GetCharacters()
            => [.. FunGameService.Characters.Select(character => new CatalogEntry(character.Id, character.GetName(), "角色"))];

        public IReadOnlyList<CatalogEntry> GetSkills()
            => [.. FunGameService.AllSkills
                .GroupBy(skill => skill.Id)
                .Select(group => group.First())
                .OrderBy(skill => skill.Id)
                .Select(skill => new CatalogEntry(skill.Id, skill.Name, GetSkillCategory(skill)))];

        public IReadOnlyList<CatalogEntry> GetItems()
            => [.. FunGameService.AllItems
                .GroupBy(item => item.Id)
                .Select(group => group.First())
                .OrderBy(item => item.Id)
                .Select(item => new CatalogEntry(item.Id, item.Name, item.ItemType.ToString()))];

        public string GetCharacterInfo(int id)
        {
            Character? template = FunGameService.Characters.ElementAtOrDefault(id - 1);
            if (id <= 0 || template is null) return "";

            Character character = template.Copy();
            character.Level = General.GameplayEquilibriumConstant.MaxLevel;
            character.NormalAttack.Level = General.GameplayEquilibriumConstant.MaxNormalAttackLevel;
            FunGameService.AddCharacterSkills(character, 1,
                General.GameplayEquilibriumConstant.MaxSkillLevel,
                General.GameplayEquilibriumConstant.MaxSuperSkillLevel);
            return character.GetInfo().Trim();
        }

        public string GetCharacterInfo(string idOrName)
            => int.TryParse(idOrName, out int id)
                ? GetCharacterInfo(id)
                : GetCharacterInfo(FunGameService.Characters.FindIndex(character =>
                    string.Equals(character.GetName(), idOrName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(character.FirstName, idOrName, StringComparison.OrdinalIgnoreCase)) + 1);

        public string GetSkillInfo(long? id, string? name)
        {
            Skill? template = FindSkill(id, name);
            if (template is null) return "";

            Character character = CreateDemoCharacter();
            List<string> messages = [$"技能展示的属性基于演示角色：[ {character.ToStringWithLevel()} ]"];
            int maximumLevel = template.IsMagic
                ? General.GameplayEquilibriumConstant.MaxMagicLevel
                : General.GameplayEquilibriumConstant.MaxSkillLevel;
            messages.Add(RenderSkill(template, character, 1));
            messages.Add(RenderSkill(template, character, 2));
            character.Level = General.GameplayEquilibriumConstant.MaxLevel;
            character.NormalAttack.Level = General.GameplayEquilibriumConstant.MaxNormalAttackLevel;
            messages.Add(RenderSkill(template, character, maximumLevel));
            return string.Join("\r\n\r\n", messages);
        }

        public string GetItemInfo(long? id, string? name)
        {
            Item? template = FindItem(id, name);
            if (template is null) return "";

            Character character = CreateDemoCharacter();
            Item item = template.Copy();
            item.Character = character;
            item.SetLevel(1);
            return $"物品展示的属性基于演示角色：[ {character.ToStringWithLevel()} ]\r\n\r\n{item.ToString(false, true)}";
        }

        public string GenerateMagicCard()
            => FunGameService.GenerateMagicCard(Random.Shared).ToString(false, true);

        public string GenerateMagicCardPack()
            => FunGameService.GenerateMagicCardPack(Random.Shared, 3)?.ToString(false, true) ?? "";

        public Task<string> GetStatsAsync(int id, bool team, CancellationToken cancellationToken = default)
            => simulations.ReadAsync(() =>
            {
                Character? character = FunGameService.Characters.ElementAtOrDefault(id - 1);
                if (id <= 0 || character is null) return "";

                Dictionary<Character, CharacterStatistics> source = team
                    ? FunGameSimulation.TeamCharacterStatistics
                    : FunGameSimulation.CharacterStatistics;
                if (!source.TryGetValue(character, out CharacterStatistics? stats)) return "";

                StringBuilder builder = new();
                builder.AppendLine(character.ToStringWithOutUser());
                builder.AppendLine($"参赛：{stats.Plays} / 胜场：{stats.Wins} / 败场：{stats.Loses} / 胜率：{stats.Winrate * 100:0.##}%");
                if (!team) builder.AppendLine($"前三：{stats.Top3s} / 前三率：{stats.Top3rate * 100:0.##}% / 场均名次：#{stats.AvgRank:0.##}");
                builder.AppendLine($"技术得分：{stats.Rating:0.0#} / MVP：{stats.MVPs}");
                builder.AppendLine($"击杀：{stats.Kills} / 死亡：{stats.Deaths} / 助攻：{stats.Assists} / 首杀：{stats.FirstKills} / 首死：{stats.FirstDeaths}");
                builder.AppendLine($"总伤害：{stats.TotalDamage:0.##} / 场均：{stats.AvgDamage:0.##} / 每秒：{stats.DamagePerSecond:0.##}");
                builder.AppendLine($"物理：{stats.TotalPhysicalDamage:0.##} / 魔法：{stats.TotalMagicDamage:0.##} / 真实：{stats.TotalTrueDamage:0.##}");
                builder.AppendLine($"总承伤：{stats.TotalTakenDamage:0.##} / 场均：{stats.AvgTakenDamage:0.##}");
                builder.AppendLine($"治疗：{stats.TotalHeal:0.##} / 护盾：{stats.TotalShield:0.##}");
                builder.AppendLine($"存活回合：{stats.LiveRound} / 行动回合：{stats.ActionTurn} / 存活时长：{stats.LiveTime:0.##}");
                builder.Append($"决策数：{stats.TurnDecisions} / 决策点：{stats.UseDecisionPoints}");
                return builder.ToString();
            }, cancellationToken);

        public Task<List<string>> GetRankingAsync(bool team, bool byRating, CancellationToken cancellationToken = default)
            => simulations.ReadAsync(() =>
            {
                Dictionary<Character, CharacterStatistics> source = team
                    ? FunGameSimulation.TeamCharacterStatistics
                    : FunGameSimulation.CharacterStatistics;
                IEnumerable<KeyValuePair<Character, CharacterStatistics>> ordered = byRating
                    ? source.OrderByDescending(entry => entry.Value.Rating)
                    : source.OrderByDescending(entry => entry.Value.Winrate);

                List<string> result = [];
                int rank = 1;
                foreach ((Character character, CharacterStatistics stats) in ordered)
                {
                    StringBuilder builder = new();
                    builder.AppendLine($"#{rank++} {character.ToStringWithOutUser()}");
                    builder.AppendLine($"参赛：{stats.Plays} / 胜场：{stats.Wins} / 胜率：{stats.Winrate * 100:0.##}%");
                    if (!team) builder.AppendLine($"前三率：{stats.Top3rate * 100:0.##}% / 场均名次：#{stats.AvgRank:0.##}");
                    builder.Append($"技术得分：{stats.Rating:0.0#} / MVP：{stats.MVPs}");
                    result.Add(builder.ToString());
                }
                return result;
            }, cancellationToken);

        private static Character CreateDemoCharacter()
        {
            Character character = FunGameService.Characters.Count > 1
                ? FunGameService.Characters[1].Copy()
                : FunGameService.Characters[0].Copy();
            character.Level = 10;
            character.NormalAttack.Level = 2;
            return character;
        }

        private static string RenderSkill(Skill template, Character character, int level)
        {
            Skill skill = template.Copy();
            skill.Character = character;
            skill.Level = level;
            return $"{character.ToStringWithLevel()}\r\n{skill.GetInfo()}";
        }

        private static Skill? FindSkill(long? id, string? name)
        {
            if (id.HasValue) return FunGameService.AllSkills.FirstOrDefault(skill => skill.Id == id.Value);
            if (!string.IsNullOrWhiteSpace(name)) return FunGameService.AllSkills.FirstOrDefault(skill =>
                string.Equals(skill.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            return null;
        }

        private static Item? FindItem(long? id, string? name)
        {
            if (id.HasValue) return FunGameService.AllItems.FirstOrDefault(item => item.Id == id.Value);
            if (!string.IsNullOrWhiteSpace(name)) return FunGameService.AllItems.FirstOrDefault(item =>
                string.Equals(item.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            return null;
        }

        private static string GetSkillCategory(Skill skill)
        {
            if (skill.IsSuperSkill) return "爆发技";
            if (skill.IsMagic) return "魔法";
            return skill.IsActive ? "主动技能" : "被动技能";
        }
    }
}
