using DroneLogger.Model;
using System;

namespace DroneLogger.Classes
{
    internal static class ProtocolPacketBuilder
    {
        public const int PacketSize = 64;
        public const double DefaultTakeoffThrottleRampPerSecond = 10.0;
        public const double MinTakeoffThrottleRampPerSecond = 1.0;
        public const double MaxTakeoffThrottleRampPerSecond = 30.0;

        public static byte[] BuildArmPacket()
        {
            var b = new byte[PacketSize];
            b[0] = 2; // Control
            b[1] = 1; // ARM
            return b;
        }

        public static byte[] BuildDisarmPacket()
        {
            var b = new byte[PacketSize];
            b[0] = 2; // Control
            b[1] = 0; // DISARM
            return b;
        }

        public static byte[] BuildTakeoffPacket()
        {
            var b = new byte[PacketSize];
            b[0] = 2; // Control
            b[1] = 2; // TAKEOFF
            return b;
        }

        public static byte[] BuildHeartbeatPacket()
        {
            var b = new byte[PacketSize];
            b[0] = 3; // Heartbeat
            return b;
        }

        public static byte[] BuildConfigurationPacket(Config config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            // Validate ranges per change request
            if (config.MinSpeed < 0 || config.MinSpeed > 100) throw new ArgumentOutOfRangeException(nameof(config.MinSpeed));
            if (config.MaxSpeed < 0 || config.MaxSpeed > 100) throw new ArgumentOutOfRangeException(nameof(config.MaxSpeed));
            if (config.MaxSpeed < config.MinSpeed) throw new ArgumentOutOfRangeException("MaxSpeed must be >= MinSpeed");
            if (config.ArmThrottle < config.MinSpeed || config.ArmThrottle > config.MaxSpeed) throw new ArgumentOutOfRangeException(nameof(config.ArmThrottle));
            if (config.MaxAngle < 1 || config.MaxAngle > 90) throw new ArgumentOutOfRangeException(nameof(config.MaxAngle));
            if (config.PIDMaxIPart < 0 || config.PIDMaxIPart > 100) throw new ArgumentOutOfRangeException(nameof(config.PIDMaxIPart));
            if (config.PIDMaxOutput < 0 || config.PIDMaxOutput > 100) throw new ArgumentOutOfRangeException(nameof(config.PIDMaxOutput));
            ValidateTakeoffThrottleRamp(config.TakeoffThrottleRampPerSecond);

            var b = new byte[PacketSize];
            b[0] = 1; // Configuration packet

            b[1] = ToByteChecked(config.ArmThrottle, nameof(config.ArmThrottle));
            b[2] = ToByteChecked(config.MinSpeed, nameof(config.MinSpeed));
            b[3] = ToByteChecked(config.MaxSpeed, nameof(config.MaxSpeed));
            b[4] = ToByteChecked(config.MaxAngle, nameof(config.MaxAngle));

            PutInt16LE(b, 5, config.TargetPitch);
            PutInt16LE(b, 7, config.TargetRoll);
            PutInt16LE(b, 9, config.TargetYaw);
            PutInt16LE(b, 11, config.TargetGz);
            // Bytes 13-14 are reserved; altitude is fixed at 50 cm.

            // Pitch
            b[15] = (byte)ScaleChecked(config.PitchKp, 100, byte.MaxValue, nameof(config.PitchKp));
            PutUInt16LE(b, 16, ScaleChecked(config.PitchKi, 10000, ushort.MaxValue, nameof(config.PitchKi)));
            PutUInt16LE(b, 18, ScaleChecked(config.PitchKd, 1000, ushort.MaxValue, nameof(config.PitchKd)));

            // Roll
            b[20] = (byte)ScaleChecked(config.RollKp, 100, byte.MaxValue, nameof(config.RollKp));
            PutUInt16LE(b, 21, ScaleChecked(config.RollKi, 10000, ushort.MaxValue, nameof(config.RollKi)));
            PutUInt16LE(b, 23, ScaleChecked(config.RollKd, 1000, ushort.MaxValue, nameof(config.RollKd)));

            // Gz
            b[25] = (byte)ScaleChecked(config.GzKp, 100, byte.MaxValue, nameof(config.GzKp));
            PutUInt16LE(b, 26, ScaleChecked(config.GzKi, 1000, ushort.MaxValue, nameof(config.GzKi)));
            PutUInt16LE(b, 28, ScaleChecked(config.GzKd, 10000, ushort.MaxValue, nameof(config.GzKd)));

            // Altitude
            b[30] = (byte)ScaleChecked(config.AltitudeKp, 100, byte.MaxValue, nameof(config.AltitudeKp));
            PutUInt16LE(b, 31, ScaleChecked(config.AltitudeKi, 1000, ushort.MaxValue, nameof(config.AltitudeKi)));
            PutUInt16LE(b, 33, ScaleChecked(config.AltitudeKd, 10000, ushort.MaxValue, nameof(config.AltitudeKd)));

            PutUInt16LE(b, 36, ScaleChecked(config.PIDMaxIPart, 10, ushort.MaxValue, nameof(config.PIDMaxIPart)));
            PutUInt16LE(b, 38, ScaleChecked(config.PIDMaxOutput, 10, ushort.MaxValue, nameof(config.PIDMaxOutput)));
            PutUInt16LE(
                b,
                41,
                ScaleChecked(
                    config.TakeoffThrottleRampPerSecond,
                    10,
                    ushort.MaxValue,
                    nameof(config.TakeoffThrottleRampPerSecond))); // 0.1 %/s

            // Removed settings leave bytes 35 and 40 zero; bytes 43-63 remain zero.
            return b;
        }

        private static void ValidateTakeoffThrottleRamp(double rampPerSecond)
        {
            if (!double.IsFinite(rampPerSecond) ||
                rampPerSecond < MinTakeoffThrottleRampPerSecond ||
                rampPerSecond > MaxTakeoffThrottleRampPerSecond)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rampPerSecond),
                    $"Takeoff throttle ramp must be between " +
                    $"{MinTakeoffThrottleRampPerSecond:F1} and " +
                    $"{MaxTakeoffThrottleRampPerSecond:F1} %/s.");
            }
        }

        private static void PutInt16LE(byte[] dest, int offset, int value)
        {
            if (value < short.MinValue || value > short.MaxValue) throw new ArgumentOutOfRangeException(nameof(value));
            short v = (short)value;
            dest[offset] = (byte)(v & 0xFF);
            dest[offset + 1] = (byte)((v >> 8) & 0xFF);
        }

        private static void PutUInt16LE(byte[] dest, int offset, int value)
        {
            if (value < 0 || value > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(value));
            ushort v = (ushort)value;
            dest[offset] = (byte)(v & 0xFF);
            dest[offset + 1] = (byte)((v >> 8) & 0xFF);
        }

        private static byte ToByteChecked(int value, string name)
        {
            if (value < byte.MinValue || value > byte.MaxValue) throw new ArgumentOutOfRangeException(name);
            return (byte)value;
        }

        private static int ScaleChecked(double value, double scale, int maximum, string name)
        {
            if (!double.IsFinite(value) || value < 0)
            {
                throw new ArgumentOutOfRangeException(name);
            }

            double scaled = Math.Round(value * scale, MidpointRounding.AwayFromZero);
            if (!double.IsFinite(scaled) || scaled < 0 || scaled > maximum)
            {
                throw new ArgumentOutOfRangeException(name);
            }

            return checked((int)scaled);
        }
    }
}
