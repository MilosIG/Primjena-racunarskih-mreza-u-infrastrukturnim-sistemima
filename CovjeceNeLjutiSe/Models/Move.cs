using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CovjeceNeLjutiSe.Models
{
    public enum MoveAction
    {
        Activate,
        Move,
        Deactivate   // uklanjanje figure
    }

    public class Move
    {
        public int PlayerId { get; set; }
        public int FigureIndex { get; set; }
        public int Steps { get; set; }
        public MoveAction Action { get; set; }
    }
}
