using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroneLogger.Model
{
    internal class Error
    {
        public enum ErrorTypes
        {
            MPU6050_I2C_Problem = 98,
            MPU6050_Device_Problem = 99
        }
        public ErrorTypes Code { get; set; }
        public object Content { get; set; } = "";
    }
}
