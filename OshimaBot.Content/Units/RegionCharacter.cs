using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;

namespace OshimaBot.Content.Units
{
    public class RegionCharacter : Character
    {
        public HashSet<Func<Region, bool>> GenerationPredicates { get; } = [];

        public RegionCharacter(long id, string name, params IEnumerable<Func<Region, bool>> predicates)
        {
            Id = id;
            Name = name;
            NickName = name;
            PrimaryAttribute = (PrimaryAttribute)Random.Next(1, 4);
            InitialATK = Random.Next(55, 101);
            InitialHP = Random.Next(80, 201);
            InitialMP = Random.Next(50, 131);

            int value = 61;
            int valueGrowth = 61;
            for (int i = 0; i < 3; i++)
            {
                if (value == 0) break;
                int attribute = i < 2 ? Random.Next(value) : (value - 1);
                int growth = i < 2 ? Random.Next(0, valueGrowth) : (valueGrowth - 1);
                switch (i)
                {
                    case 1:
                        InitialAGI = attribute;
                        AGIGrowth = Calculation.Round(Convert.ToDouble(growth) / 10, 2);
                        break;
                    case 2:
                        InitialINT = attribute;
                        INTGrowth = Calculation.Round(Convert.ToDouble(growth) / 10, 2);
                        break;
                    case 0:
                    default:
                        InitialSTR = attribute;
                        STRGrowth = Calculation.Round(Convert.ToDouble(growth) / 10, 2);
                        break;
                }
                value -= attribute;
                valueGrowth -= growth;
            }
            InitialSPD = Random.Next(220, 451);
            InitialHR = Random.Next(3, 9);
            InitialMR = Random.Next(3, 9);
            foreach (Func<Region, bool> predicate in predicates)
            {
                GenerationPredicates.Add(predicate);
            }
        }
    }
}
