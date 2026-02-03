using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CovjeceNeLjutiSe.Models
{
    public class Player
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Index { get; set; }      // redni broj igraca

        public int StartPosition { get; set; }
        public int GoalPosition { get; set; }

        public List<Figure> Figures { get; set; } = new();
    }
}
