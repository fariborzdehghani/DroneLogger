using System.Buffers.Binary;
using System.IO;

namespace DroneLogger.Classes;

internal static class LiveSerialProtocol
{
    public const string Marker = "LiveLog=";
    public const int BlobSize = 128 + FlightLogProtocol.Srf05TraceRecordSize;
    public const int PacketSize = BlobSize + 2;
    public const int EncodedSize = 4 * ((PacketSize + 2) / 3);
    public const int IntervalMs = 100;

    public static FlightLogSession DecodeLine(string line)
    {
        if (!line.StartsWith(Marker, StringComparison.Ordinal) || line.Length != Marker.Length + EncodedSize)
            throw new InvalidDataException("Invalid full-precision serial frame length.");
        byte[] packet;
        try { packet = Convert.FromBase64String(line[Marker.Length..]); }
        catch (FormatException ex) { throw new InvalidDataException("Invalid serial frame encoding.", ex); }
        if (packet.Length != PacketSize || BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(BlobSize)) !=
            FlightLogProtocol.Crc16(packet.AsSpan(0, BlobSize)))
            throw new InvalidDataException("Serial frame checksum failed; sample discarded.");
        return FlightLogSession.Decode(packet[..BlobSize], liveSample: true);
    }
}
