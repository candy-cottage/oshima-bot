using FunGame.Core.Entity;

namespace OshimaBot.Content.Units
{
    public class RegionUnit : Unit
    {
        public override bool IsUnit => false; // 不走单位判断
        public HashSet<Func<Region, bool>> GenerationPredicates { get; } = [];

        public RegionUnit(long id, string name, params IEnumerable<Func<Region, bool>> predicates)
        {
            Id = id;
            Name = name;
            InitialATK = Random.Next(25, 51);
            InitialHP = Random.Next(35, 91);
            InitialMP = Random.Next(20, 61);
            InitialSPD = Random.Next(155, 320);
            InitialHR = Random.Next(1, 6);
            InitialMR = Random.Next(1, 6);
            foreach (Func<Region, bool> predicate in predicates)
            {
                GenerationPredicates.Add(predicate);
            }
        }
    }
}
