using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace DroneLogger.Classes
{
    internal class Tools
    {
        public static int ParseInt(string input, string fieldName)
        {
            if (!int.TryParse(input, out int result))
                throw new ArgumentException($"Invalid value for {fieldName}");
            return result;
        }

        public static double ParseDouble(string input, string fieldName)
        {
            if (!double.TryParse(input, out double result))
                throw new ArgumentException($"Invalid value for {fieldName}");
            return result;
        }

        public static void Log(MainWindow context, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            context.Dispatcher.BeginInvoke(() =>
            {
                if (context.chk_GeneralLog.IsChecked == true)
                {
                    if (context.txt_Log.Document.Blocks.Count > 1000)
                    {
                        context.txt_Log.Document.Blocks.Clear();
                    }
                    context.txt_Log.AppendText(value + Environment.NewLine);
                    context.txt_Log.ScrollToEnd();
                }
            });
        }
    }
}
