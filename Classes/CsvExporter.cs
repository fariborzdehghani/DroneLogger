using System;
using System.IO;
using System.Text;
using DroneLogger.Model;
using CsvHelper;
using System.Globalization;

namespace DroneLogger.Classes
{
    public class CsvExporter
    {
        public static void ExportSession(SessionData session, string basePath)
        {
            try
            {
                string timestamp = session.StartTime.ToString("yyyyMMdd_HHmmss");
                string filename = Path.Combine(basePath, $"session_{timestamp}.csv");

                using (var writer = new StreamWriter(filename, false, Encoding.UTF8))
                using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                {
                    // Write header and records automatically based on LogData properties
                    csv.WriteHeader<LogData>();
                    csv.NextRecord();
                    foreach (var point in session.DataPoints)
                    {
                        csv.WriteRecord(point);
                        csv.NextRecord();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error exporting session data: {ex.Message}", ex);
            }
        }
    }
}