using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CovjeceNeLjutiSe.Models
{
    public class GameReport
    {
        public List<Player> Players { get; set; } = new();
        public int CurrentPlayerIndex { get; set; }
        public bool IsFinished { get; set; }
    }
}
