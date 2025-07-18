using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroneLogger.Model
{
    internal class Position
    {
        public double Roll { get; set; }
        public double Pitch { get; set; }
        public double Gz { get; set; }
        public double Vertical_Velocity { get; set; }
        public double Vertical_Acc { get; set; }
    }
}
