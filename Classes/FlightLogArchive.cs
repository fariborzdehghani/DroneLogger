using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CsvHelper;

namespace DroneLogger.Classes;

internal static class FlightLogArchive
{
    public static string RootFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DroneLogger", "FlightLogs");

    public static string Save(byte[] blob, FlightLogSession session, string? rootFolder = null)
    {
        string folder = Path.Combine(rootFolder ?? RootFolder, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_flight_{session.SessionId}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        byte[] file = new byte[8 + blob.Length];
        "FDL1"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), FlightLogProtocol.Crc32(blob));
        blob.CopyTo(file, 8);
        File.WriteAllBytes(Path.Combine(folder, "flight.flightlog"), file);
        File.WriteAllText(Path.Combine(folder, "flight.json"), JsonSerializer.Serialize(session,
            new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals }));
        SaveCsv(Path.Combine(folder, "samples.csv"), session);
        File.WriteAllText(Path.Combine(folder, "diagnostics.txt"),
            $"Average loop dt since startup: {session.AverageLoopDtMs?.ToString("F3", CultureInfo.InvariantCulture) ?? "unavailable"} ms\n" +
            $"Loop iterations: {session.LoopIterationCount}\n" +
            string.Join(Environment.NewLine, session.Messages.Select(m =>
                $"[{m.TimestampMs} ms] {(m.IsError ? "Error" : "Information")} {m.Code}: {m.Message}")));
        return folder;
    }

    public static string CreateCsv(FlightLogSession session)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        csv.WriteRecords(session.Samples);
        csv.Flush();
        return writer.ToString();
    }

    public static void SaveCsv(string path, FlightLogSession session) =>
        File.WriteAllText(path, CreateCsv(session));

    public static FlightLogSession Load(string path)
    {
        var length = new FileInfo(path).Length;
        if (length < 136 || length > 8 + FlightLogProtocol.MaxBlobSize)
            throw new InvalidDataException("Flight-log file size is invalid.");
        byte[] file = File.ReadAllBytes(path);
        if (!file.AsSpan(0, 4).SequenceEqual("FDL1"u8) ||
            FlightLogSession.U32(file, 4) != FlightLogProtocol.Crc32(file.AsSpan(8)))
            throw new InvalidDataException("Flight-log file is damaged or unsupported.");
        return FlightLogSession.Decode(file[8..]);
    }
}
