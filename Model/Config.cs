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
        // Keep the legacy JSON name so existing droneconfig.json files load.
        [JsonPropertyName("Throttle")]
        public int ArmThrottle { get; set; }
        public int HoverThrottle { get; set; }
        public int MinThrottle { get; set; }
        public int MaxThrottle { get; set; }
        public int TakeoffAltitudeCm { get; set; } =
            Classes.ProtocolPacketBuilder.DefaultTakeoffAltitudeCm;
        public int MaxAngle { get; set; }
        public int TargetPitch { get; set; }
        public int TargetRoll { get; set; }
        public int TargetYaw { get; set; }
        public int TargetGz { get; set; }
        public double PitchKp { get; set; }
        public double PitchKi { get; set; }
        public double PitchKd { get; set; }
        public double RollKp { get; set; }
        public double RollKi { get; set; }
        public double RollKd { get; set; }
        public double GzKp { get; set; }
        public double GzKi { get; set; }
        public double GzKd { get; set; }
        public double AltitudeKp { get; set; }
        public double AltitudeKi { get; set; }
        public double AltitudeKd { get; set; }
        public double PIDMaxIPart { get; set; }
        public double PIDMaxOutput { get; set; }
        public double TakeoffThrottleRampPerSecond { get; set; } =
            Classes.ProtocolPacketBuilder.DefaultTakeoffThrottleRampPerSecond;
    }
}
