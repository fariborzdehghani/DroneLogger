using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace DroneLogger.Model
{
    public class Config
    {
        public int Throttle { get; set; }
        public int MinSpeed { get; set; }
        public int MaxSpeed { get; set; }
        public int MaxAngle { get; set; }
        public int TargetPitch { get; set; }
        public int TargetRoll { get; set; }
        public int TargetYaw { get; set; }
        public double PitchKp { get; set; }
        public double PitchKi { get; set; }
        public double PitchKd { get; set; }
        public double RollKp { get; set; }
        public double RollKi { get; set; }
        public double RollKd { get; set; }
        public double YawKp { get; set; }
        public double YawKi { get; set; }
        public double YawKd { get; set; }
        public double PIDStartThreshold { get; set; }
        public double PIDMaxIPart { get; set; }
        public double PIDMaxOutput { get; set; }
        public int PIDStartThrottle { get; set; }
        public int DefaultThrottle { get; internal set; }
    }
}
