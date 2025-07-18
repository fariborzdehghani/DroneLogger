using DroneLogger.Classes;
using System;
using System.Collections.Generic;

namespace DroneLogger.Model
{
    public class SessionData
    {
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public Config Config { get; set; }
        public List<LogData> DataPoints { get; set; } = new List<LogData>();
    }
}