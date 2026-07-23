using System;

namespace DroneLogger.Classes
{
    public class LogData
    {
        public double m1 { get; set; }
        public double m2 { get; set; }
        public double m3 { get; set; }
        public double m4 { get; set; }
        public double roll { get; set; }
        public double pitch { get; set; }
        public double roll_p { get; set; }
        public double roll_i { get; set; }
        public double roll_d { get; set; }
        public double pitch_p { get; set; }
        public double pitch_i { get; set; }
        public double pitch_d { get; set; }
        public double yaw { get; set; }
        public double Gz { get; set; }
        public double altitude { get; set; }
        public double base_throttle { get; set; }
        public double Vz { get; set; }
        public uint Vz_valid { get; set; }
        public double Gz_p { get; set; }
        public double Gz_i { get; set; }
        public double Gz_d { get; set; }
        public double altitude_p { get; set; }
        public double altitude_i { get; set; }
        public double altitude_d { get; set; }
        public double PitchKp { get; internal set; }
        public double roll_total { get; set; }
        public double pitch_total { get; set; }

        public override string ToString()
        {
            return $"Data={{\"roll\":{roll:F2},\"pitch\":{pitch:F2}," +
                   $"\"yaw\":{yaw:F2},\"altitude\":{altitude:F2},\"base_throttle\":{base_throttle:F2}," +
                   $"\"m1\":{m1:F2},\"m2\":{m2:F2},\"m3\":{m3:F2},\"m4\":{m4:F2}," +
                   $"\"roll_p\":{roll_p:F4},\"roll_i\":{roll_i:F4},\"roll_d\":{roll_d:F4}," +
                   $"\"pitch_p\":{pitch_p:F4},\"pitch_i\":{pitch_i:F4},\"pitch_d\":{pitch_d:F4}}}";
        }
    }
}
