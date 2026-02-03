using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CovjeceNeLjutiSe.Models
{
    public class GameState
    {
        public List<Player> Players { get; set; } = new();
        public int CurrentPlayerIndex { get; set; } = 0;
        public bool IsFinished { get; set; } = false;

        public int FiguresPerPlayer { get; set; } = 4;
        public int BoardSize { get; set; } = 40;
    }
}
