using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CovjeceNeLjutiSe.Models
{
    public class Figure
    {
        public bool IsActive { get; set; } = false;

        // Lokalna pozicija: HOME = -1, tabla = 0..38, FINISH = 39
        public int Position { get; set; } = -1;

        public int StepsFromStart { get; set; } = 0;

        // Nije nužno za pravila, ali korisno za prikaz
        public int DistanceToGoal { get; set; } = 39;

        // EXIT/SAFE HOUSE stanje
        public bool IsFinished { get; set; } = false;
    }
}