using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroneLogger.Model
{
    internal class Information
    {
        public enum MessageType
        {
            TaskDone = 1001,
            TaskOngoing = 1002,
            Config = 1003,
            Command = 1004,
            ConfigSet = 1005
        }
        public MessageType Code { get; set; }
        public object Content { get; set; } = "";
    }
}
