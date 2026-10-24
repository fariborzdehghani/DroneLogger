using System.Buffers.Binary;

namespace DroneLogger.Classes;

internal static class FlightLogProtocol
{
    public const byte Version = 1, Begin = 1, Read = 2, End = 3;
    public const byte CompactProfile = 0, FullProfile = 1;
    public const int PayloadSize = 48, HeaderSize = 64, RecordSize = 124, Capacity = 512;
    public const int CompactRecordSize = 50;
    public const int TraceRecordSize = 192, AttitudeTraceSize = 68;
    // Formats 6 and 7 share the same 16-byte trace and maximum download size.
    public const int Srf05TraceSize = 16, Srf05TraceRecordSize = TraceRecordSize + Srf05TraceSize;
    public const int LegacyRecordSize = 116, LegacyCapacity = 256;
    public const int EventCapacity = 16, EventSize = 104;
    public const int MaxBlobSize = HeaderSize + 64 + Capacity * Srf05TraceRecordSize + EventCapacity * EventSize;
    public const uint BeginSequence = uint.MaxValue, EndSequence = uint.MaxValue - 1;

    public static byte[] BuildRequest(byte operation, uint transferId, uint sequence = 0, byte profile = CompactProfile)
    {
        if (operation < Begin || operation > End || transferId == 0 ||
            (operation != Read && sequence != 0) ||
            (operation == Begin ? profile > FullProfile : profile != CompactProfile))
            throw new ArgumentOutOfRangeException(nameof(operation));
        var packet = new byte[ProtocolPacketBuilder.PacketSize];
        packet[0] = 4;
        packet[1] = operation;
        packet[2] = Version;
        packet[3] = profile;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), transferId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), sequence);
        Seal(packet);
        return packet;
    }

    public static void Seal(byte[] packet) =>
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(62), Crc16(packet.AsSpan(0, 62)));

    public static bool TryDecodeLine(string line, out byte[] packet)
    {
        packet = [];
        const string marker = "FlightLog=";
        int index = line.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0 || (index > 0 && !char.IsWhiteSpace(line[index - 1]))) return false;
        string hex = line[(index + marker.Length)..].Trim();
        if (hex.Length != 128) return false;
        try { packet = Convert.FromHexString(hex); }
        catch (FormatException) { return false; }
        return packet[0] == 5 && packet[1] <= 4 && packet[2] == Version &&
            packet[3] <= PayloadSize &&
            BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(62)) == Crc16(packet.AsSpan(0, 62));
    }

    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xffff;
        foreach (byte value in data)
        {
            crc ^= (ushort)(value << 8);
            for (int bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        return crc;
    }

    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
        }
        return ~crc;
    }
}
