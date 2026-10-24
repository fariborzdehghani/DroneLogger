using System;

namespace DroneLogger.Classes
{
    public class LogData
    {
        public uint timestamp_ms { get; set; }
        public double elapsed_ms { get; set; }
        public byte state { get; set; }
        public string state_name => landing_active && state is 3 or 4 ? "LANDING" : state switch
        {
            0 => "INIT", 1 => "CONFIGURED", 2 => "ARMED", 3 => "TAKEOFF",
            4 => "FLYING", 5 => "FAULT", _ => "UNKNOWN"
        };
        public ushort error_code { get; set; }
        public bool altitude_valid { get; set; } = true;
        public bool altitude_control_active { get; set; }
        public bool mixer_limited { get; set; }
        public bool landing_active { get; set; }
        public double loop_dt_ms { get; set; }
        public double altitude_target { get; set; }
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
        public double roll_rate_dps { get; set; } = double.NaN;
        public double pitch_rate_dps { get; set; } = double.NaN;
        public bool attitude_rates_valid { get; set; }
        public bool? gyro_based_damping { get; set; }
        public bool srf05_trace_valid { get; set; }
        public uint? srf05_last_echo_ms { get; set; }
        public uint? srf05_last_accepted_ms { get; set; }
        public uint? srf05_age_ms { get; set; }
        public byte? srf05_result { get; set; }
        public double? srf05_raw_distance_cm { get; set; }
        public byte? srf05_filter_result { get; set; }
        public string srf05_filter_status => srf05_filter_result switch
        {
            0 => "Invalid filter input", 1 => "Accepted", 2 => "Rejected due to tilt",
            3 => "Waiting for three echoes", 4 => "Duplicate or old timestamp",
            5 => "Rejected by median motion gate", 6 => "Rejected by prediction innovation gate",
            7 => "Invalid filter state", _ => "Not recorded"
        };
        public string srf05_status => !srf05_trace_valid ? "Unavailable" : srf05_result switch
        {
            0 => "Waiting for echo", 1 => "Accepted", 2 => "Rejected by altitude filter",
            3 => "Rejected due to tilt", 4 => "Echo timeout", 5 => "Read error",
            6 => "Start error", _ => "Unavailable"
        };
        public bool imu_trace_valid { get; set; }
        public uint? imu_sequence { get; set; }
        public uint? imu_timestamp_ms { get; set; }
        public double imu_dt_ms { get; set; } = double.NaN;
        public double accel_x_g { get; set; } = double.NaN;
        public double accel_y_g { get; set; } = double.NaN;
        public double accel_z_g { get; set; } = double.NaN;
        public double accel_norm_g => imu_trace_valid ? Math.Sqrt(accel_x_g * accel_x_g + accel_y_g * accel_y_g + accel_z_g * accel_z_g) : double.NaN;
        public double roll_previous_deg { get; set; } = double.NaN;
        public double roll_predicted_deg { get; set; } = double.NaN;
        public double roll_accel_deg { get; set; } = double.NaN;
        public double roll_bias_dps { get; set; } = double.NaN;
        public double roll_correction_deg { get; set; } = double.NaN;
        public double pitch_previous_deg { get; set; } = double.NaN;
        public double pitch_predicted_deg { get; set; } = double.NaN;
        public double pitch_accel_deg { get; set; } = double.NaN;
        public double pitch_bias_dps { get; set; } = double.NaN;
        public double pitch_correction_deg { get; set; } = double.NaN;
        public byte? roll_estimator_flags { get; set; }
        public byte? pitch_estimator_flags { get; set; }
        public byte? imu_timing_flags { get; set; }
        public string roll_estimator_status => imu_trace_valid ? DescribeEstimatorFlags(roll_estimator_flags) : "Unavailable";
        public string pitch_estimator_status => imu_trace_valid ? DescribeEstimatorFlags(pitch_estimator_flags) : "Unavailable";
        public string imu_timing_status
        {
            get
            {
                if (!imu_trace_valid || imu_timing_flags == null) return "Unavailable";
                string[] labels = ["Zero dt clamped to 1 ms", "Long dt clamped to 100 ms", "Gyro reached sensor limit", "Acceleration reached sensor limit"];
                return imu_timing_flags == 0 ? "No timing or clipping flags" :
                    string.Join(", ", labels.Where((_, bit) => (imu_timing_flags.Value & (1 << bit)) != 0));
            }
        }

        private static string DescribeEstimatorFlags(byte? flags)
        {
            if (flags == null) return "Unavailable";
            string[] labels = ["Correction applied", "Acceleration rejected", "Angle correction rejected", "Recovery waiting",
                "Recovery applied", "Filter state reset", "Angle wrapped", "Legacy angle reset"];
            var selected = labels.Where((_, bit) => (flags.Value & (1 << bit)) != 0);
            return flags == 0 ? "Gyro prediction only" : string.Join(", ", selected);
        }
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
