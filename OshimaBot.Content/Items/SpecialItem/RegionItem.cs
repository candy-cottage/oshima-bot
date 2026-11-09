using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;

namespace OshimaBot.Content.Items
{
    public class RegionItem : Item
    {
        public HashSet<Func<Region, bool>> GenerationPredicates { get; } = [];

        public RegionItem(long id, string name, string description, string story = "", QualityType quality = QualityType.White, params IEnumerable<Func<Region, bool>> predicates) : base(ItemType.SpecialItem)
        {
            Id = id;
            Name = name;
            Description = description;
            BackgroundStory = story;
            QualityType = quality;
            foreach (Func<Region, bool> predicate in predicates)
            {
                GenerationPredicates.Add(predicate);
            }
        }
    }
}
