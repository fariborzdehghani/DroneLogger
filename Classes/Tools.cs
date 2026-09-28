using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace DroneLogger.Classes
{
    internal class Tools
    {
        private const int MaxPendingLogLines = 1000;
        private const int MaxLinesPerFlush = 100;
        private const int LogFlushIntervalMs = 100;
        private const int MaxDisplayedLogCharacters = 50000;
        private const int DisplayedLogCharactersAfterTrim = 40000;

        private readonly struct PendingLogLine
        {
            public PendingLogLine(string text, bool isDataLog)
            {
                Text = text;
                IsDataLog = isDataLog;
            }

            public string Text { get; }
            public bool IsDataLog { get; }
        }

        private static readonly ConcurrentQueue<PendingLogLine> PendingLogLines = new();
        private static int pendingLogLineCount;
        private static int logFlushScheduled;

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

        public static void Log(
            MainWindow context,
            string value,
            bool isDataLog = false)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            PendingLogLines.Enqueue(new PendingLogLine(value, isDataLog));
            int queuedLines = Interlocked.Increment(ref pendingLogLineCount);

            while (queuedLines > MaxPendingLogLines &&
                   PendingLogLines.TryDequeue(out _))
            {
                queuedLines = Interlocked.Decrement(ref pendingLogLineCount);
            }

            ScheduleLogFlush(context);
        }

        public static void ClearLog(MainWindow context)
        {
            while (PendingLogLines.TryDequeue(out _))
            {
                Interlocked.Decrement(ref pendingLogLineCount);
            }

            context.txt_Log.Clear();
        }

        private static void ScheduleLogFlush(MainWindow context)
        {
            if (context.Dispatcher.HasShutdownStarted ||
                context.Dispatcher.HasShutdownFinished ||
                Interlocked.CompareExchange(ref logFlushScheduled, 1, 0) != 0)
            {
                return;
            }

            _ = FlushLogAfterDelayAsync(context);
        }

        private static async Task FlushLogAfterDelayAsync(MainWindow context)
        {
            try
            {
                await Task.Delay(LogFlushIntervalMs).ConfigureAwait(false);
                if (context.Dispatcher.HasShutdownStarted ||
                    context.Dispatcher.HasShutdownFinished)
                {
                    Interlocked.Exchange(ref logFlushScheduled, 0);
                    return;
                }

                await context.Dispatcher.InvokeAsync(
                    () => FlushLog(context),
                    DispatcherPriority.Background);
            }
            catch (TaskCanceledException)
            {
                Interlocked.Exchange(ref logFlushScheduled, 0);
            }
            catch (InvalidOperationException)
            {
                Interlocked.Exchange(ref logFlushScheduled, 0);
            }
        }

        private static void FlushLog(MainWindow context)
        {
            var batch = new StringBuilder();

            for (int line = 0;
                 line < MaxLinesPerFlush &&
                 PendingLogLines.TryDequeue(out PendingLogLine pendingLine);
                 line++)
            {
                Interlocked.Decrement(ref pendingLogLineCount);
                if (!pendingLine.IsDataLog ||
                    context.chk_LogData.IsChecked == true)
                {
                    batch.AppendLine(pendingLine.Text);
                }
            }

            if (context.chk_GeneralLog.IsChecked == true && batch.Length > 0)
            {
                context.txt_Log.AppendText(batch.ToString());
                TrimDisplayedLog(context);
                context.txt_Log.CaretIndex = context.txt_Log.Text.Length;
                context.txt_Log.ScrollToEnd();
            }

            Interlocked.Exchange(ref logFlushScheduled, 0);
            if (!PendingLogLines.IsEmpty)
            {
                ScheduleLogFlush(context);
            }
        }

        private static void TrimDisplayedLog(MainWindow context)
        {
            string displayedText = context.txt_Log.Text;
            if (displayedText.Length <= MaxDisplayedLogCharacters)
            {
                return;
            }

            int trimStart = displayedText.Length -
                            DisplayedLogCharactersAfterTrim;
            int nextLine = displayedText.IndexOf('\n', trimStart);
            context.txt_Log.Text =
                nextLine >= 0
                    ? displayedText[(nextLine + 1)..]
                    : displayedText[^DisplayedLogCharactersAfterTrim..];
        }
    }
}
