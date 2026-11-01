using DroneLogger.Classes;
using DroneLogger.Model;

namespace DroneLogger;

public partial class MainWindow
{
    private readonly SerialFlightRecorder serialRecorder = new();
    internal SerialFlightRecorder SerialRecorder => serialRecorder;
    private Config? sentConfiguration;
    private Task serialSaveTask = Task.CompletedTask;
    private bool fullSerialRecordingShown;

    internal void BeginSerialFlightRecording()
    {
        if (!logger.IsLogPortOpen() || serialRecorder.IsRecording) return;
        serialRecorder.Start(sentConfiguration, sentConfiguration != null);
        fullSerialRecordingShown = false;
        lbl_FlightLogStatus.Text = "Recording serial flight · ARM to DISARM";
    }

    internal void RecordSerialTelemetry(LogData sample, bool hasTimestamp, long receivedAt, FlightLogSession? metadata = null)
    {
        serialRecorder.Record(sample, hasTimestamp, receivedAt, metadata);
        if (metadata != null && serialRecorder.IsRecording && !fullSerialRecordingShown)
        {
            fullSerialRecordingShown = true;
            lbl_FlightLogStatus.Text = "Recording serial flight · full precision + sensor traces";
        }
    }
    internal void RecordSerialMessage(ushort code, bool error, string text, long receivedAt) => serialRecorder.RecordMessage(code, error, text, receivedAt);

    private void FinishSerialFlightRecording(string reason)
    {
        FlightLogSession? flight = serialRecorder.Finish(reason);
        if (flight == null) return;
        serialSaveTask = SaveSerialFlightAsync(flight, serialSaveTask);
    }

    private async Task SaveSerialFlightAsync(FlightLogSession flight, Task previousSave)
    {
        await previousSave;
        try
        {
            string path = await Task.Run(() => FlightLogArchive.SaveSerial(flight));
            Tools.Log(this, $"Serial flight saved: {path}");
            if (shutdownInProgress || serialRecorder.IsRecording || transmitter.IsArmed) return;
            if (flight.Samples.Count > 0) ShowRecordedFlight(flight);
            flightLogFilePath.Text = path;
            lbl_FlightLogStatus.Text = flight.Samples.Count > 0
                ? "Serial flight saved · ready to review" : "Saved ARM/DISARM session · no telemetry received";
            lbl_FlightLogStatus.ToolTip = path;
        }
        catch (Exception ex)
        {
            Tools.Log(this, $"Cannot save serial flight: {ex.Message}");
            lbl_FlightLogStatus.Text = $"Serial flight save failed: {ex.Message}";
        }
    }
}
