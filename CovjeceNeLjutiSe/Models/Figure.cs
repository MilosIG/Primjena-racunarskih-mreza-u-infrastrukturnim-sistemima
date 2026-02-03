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
        public int Position { get; set; } = -1;

        public int StepsFromStart { get; set; } = 0;
        public int DistanceToGoal { get; set; } = 0;

        public bool IsFinished { get; set; } = false;
    }
}
