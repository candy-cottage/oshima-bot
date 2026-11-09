using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;

namespace OshimaBot.Content.Characters
{
    public class Ryuko : Character
    {
        public Ryuko() : base()
        {
            Id = 17;
            Name = "Ryu";
            FirstName = "ko";
            NickName = "流";
            PrimaryAttribute = PrimaryAttribute.AGI;
            InitialATK = 22;
            InitialHP = 100;
            InitialMP = 45;
            InitialSTR = 11;
            STRGrowth = 0.8;
            InitialAGI = 15;
            AGIGrowth = 1.9;
            InitialINT = 4;
            INTGrowth = 0.3;
            InitialSPD = 300;
            InitialHR = 4;
            InitialMR = 2;
        }
    }
}
