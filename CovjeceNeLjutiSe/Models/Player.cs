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

        // redni broj igraca (1..N)
        public int Index { get; set; }

        // start offset na apsolutnoj tabli (0,10,20,30 za 40)
        public int StartPosition { get; set; }

        public List<Figure> Figures { get; set; } = new();

        // izvedeno: broj figura u SAFE HOUSE (IsFinished=true); sinhronizuje server
        public int SafeHouse { get; set; } = 0;
    }
}
