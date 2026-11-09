using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;

namespace OshimaBot.Content.Characters
{
    public class CustomCharacter : Character
    {
        public CustomCharacter(long user_id, string name, string firstname = "", string nickname = "", PrimaryAttribute primaryAttribute = PrimaryAttribute.None) : base()
        {
            Id = user_id;
            Name = name;
            FirstName = firstname;
            NickName = nickname;
            PrimaryAttribute = (PrimaryAttribute)Random.Next(1, 4);
            InitialATK = Random.Next(15, 26);
            InitialHP = Random.Next(40, 86);
            InitialMP = Random.Next(20, 56);

            int reduce = 0;
            int reduceGrowth = 0;
            if (primaryAttribute != PrimaryAttribute.None)
            {
                int attribute = Random.Next(15, 31);
                int growth = Random.Next(15, 31);
                switch (primaryAttribute)
                {
                    case PrimaryAttribute.STR:
                        InitialSTR = attribute;
                        STRGrowth = Calculation.Round(Convert.ToDouble(growth) / 10, 2);
                        break;
                    case PrimaryAttribute.AGI:
                        InitialAGI = attribute;
                        AGIGrowth = Calculation.Round(Convert.ToDouble(growth) / 10, 2);
                        break;
                    case PrimaryAttribute.INT:
                        InitialINT = attribute;
                        INTGrowth = Calculation.Round(Convert.ToDouble(growth) / 10, 2);
                        break;
                }
                PrimaryAttribute = primaryAttribute;
                reduce = attribute;
                reduceGrowth = growth;
            }

            int value = 31 - reduce;
            int valueGrowth = 31 - reduceGrowth;
            for (int i = 0; i < 3; i++)
            {
                if (value == 0) break;
                int attribute = i < 2 ? Random.Next(value) : (value - 1);
                int growth = i < 2 ? Random.Next(0, valueGrowth) : (valueGrowth - 1);
                switch (i)
                {
                    case 1:
                        if (primaryAttribute == PrimaryAttribute.AGI) continue;
                        InitialAGI = attribute;
                        AGIGrowth = Calculation.Round(Convert.ToDouble(growth) / 10, 2);
                        break;
                    case 2:
                        if (primaryAttribute == PrimaryAttribute.INT) continue;
                        InitialINT = attribute;
                        INTGrowth = Calculation.Round(Convert.ToDouble(growth) / 10, 2);
                        break;
                    case 0:
                    default:
                        if (primaryAttribute == PrimaryAttribute.STR) continue;
                        InitialSTR = attribute;
                        STRGrowth = Calculation.Round(Convert.ToDouble(growth) / 10, 2);
                        break;
                }
                value -= attribute;
                valueGrowth -= growth;
            }
            InitialSPD = Random.Next(220, 291);
            InitialHR = Random.Next(1, 6);
            InitialMR = Random.Next(1, 6);
        }
    }
}
