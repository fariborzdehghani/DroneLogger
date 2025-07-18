using System;
using System.Collections.Generic;
using System.IO;
using DroneLogger.Model;

namespace DroneLogger.Classes
{
    public class SessionManager
    {
        public readonly Logger logger;
        private SessionData currentSession;
        private readonly PidPlotter pidPlotter;
        private const string SESSION_FILE_NAME = "last_session.csv";

        public bool IsSessionActive { get; private set; }
        public SessionData CurrentSession => currentSession;
        public List<SessionData> SessionHistory { get; } = new List<SessionData>();

        public SessionManager(Logger logger)
        {
            this.logger = logger;
            // Get PidPlotter instance from Logger to ensure we're using the same instance
            this.pidPlotter = logger.GetPidPlotter();
        }

        public void StartNewSession(Config currentConfig = null)
        {
            // End current session if active
            if (IsSessionActive)
            {
                EndCurrentSession();
            }

            // Create and start new session
            currentSession = new SessionData
            {
                StartTime = DateTime.Now,
                DataPoints = new List<LogData>(),
                Config = currentConfig
            };

            IsSessionActive = true;

            // Reset plotting
            pidPlotter.Reset();
        }

        public void EndCurrentSession()
        {
            if (IsSessionActive && currentSession != null)
            {
                currentSession.EndTime = DateTime.Now;

                // Save the current session to CSV, using the application's directory
                try
                {
                    string basePath = AppDomain.CurrentDomain.BaseDirectory;
                    CsvExporter.ExportSession(currentSession, basePath);
                }
                catch (Exception ex)
                {
                    // Log error but don't prevent session from ending
                    Tools.Log(logger.context, $"Failed to save session data: {ex.Message}");
                }

                IsSessionActive = false;
                currentSession = null;
            }
        }

        public void AddDataPoint(LogData dataPoint)
        {
            if (IsSessionActive && currentSession != null)
            {
                currentSession.DataPoints.Add(dataPoint);
                // PID plotting is handled in Logger.cs's AnalyseData method
            }
        }

        public void ClearAllSessions()
        {
            EndCurrentSession();
            SessionHistory.Clear();
            currentSession = null;
            IsSessionActive = false;
            pidPlotter.Reset();
        }
    }
}