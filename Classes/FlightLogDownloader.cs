using System.IO;
using System.Security.Cryptography;
using System.Threading.Channels;

namespace DroneLogger.Classes;

internal sealed class FlightLogDownloader
{
    private readonly Func<byte[], CancellationToken, Task> send;
    private readonly Channel<byte[]> responses = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private int running;
    internal TimeSpan ResponseTimeout { get; set; } = TimeSpan.FromMilliseconds(1000);

    public FlightLogDownloader(Func<byte[], CancellationToken, Task> send) => this.send = send;

    public bool AcceptLine(string line)
    {
        if (!line.StartsWith("FlightLog=", StringComparison.Ordinal)) return false;
        if (Volatile.Read(ref running) != 0 && FlightLogProtocol.TryDecodeLine(line, out var packet))
            responses.Writer.TryWrite(packet);
        return true; // Suppress bulk transfer frames in the user-visible diagnostic log.
    }

    public async Task<byte[]> DownloadAsync(IProgress<FlightLogDownloadProgress>? progress, CancellationToken ct, bool fullPrecision = false)
    {
        if (Interlocked.Exchange(ref running, 1) != 0)
            throw new InvalidOperationException("A flight-log download is already running.");
        uint transferId;
        do { transferId = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4)); } while (transferId == 0);
        try
        {
            while (responses.Reader.TryRead(out _)) { }
            byte[] begin = await ExchangeAsync(FlightLogProtocol.Begin, transferId, 0, ct,
                fullPrecision ? FlightLogProtocol.FullProfile : FlightLogProtocol.CompactProfile).ConfigureAwait(false);
            if (begin[3] != 12) throw new InvalidDataException("Invalid BEGIN response.");
            uint size = FlightLogSession.U32(begin, 12), expectedCrc = FlightLogSession.U32(begin, 16);
            uint sessionId = FlightLogSession.U32(begin, 20);
            if (size < 128 || size > FlightLogProtocol.MaxBlobSize)
                throw new InvalidDataException("Flight-log size exceeds the recorder capacity.");
            byte[] blob = new byte[(int)size];
            progress?.Report(new FlightLogDownloadProgress(0, blob.Length));
            int offset = 0;
            for (uint sequence = 0; offset < blob.Length; sequence++)
            {
                byte[] response = await ExchangeAsync(FlightLogProtocol.Read, transferId, sequence, ct).ConfigureAwait(false);
                int length = Math.Min(FlightLogProtocol.PayloadSize, blob.Length - offset);
                if (response[3] != length) throw new InvalidDataException("Flight-log chunk has the wrong length.");
                response.AsSpan(12, length).CopyTo(blob.AsSpan(offset));
                offset += length;
                progress?.Report(new FlightLogDownloadProgress(offset, blob.Length));
            }
            if (FlightLogProtocol.Crc32(blob) != expectedCrc)
                throw new InvalidDataException("Flight-log checksum failed. Download it again.");
            FlightLogSession decoded = FlightLogSession.Decode(blob);
            if (decoded.SessionId != sessionId) throw new InvalidDataException("Flight-log session changed during download.");
            return blob;
        }
        finally
        {
            /* Best effort release, including after Cancel. Firmware also expires
             * abandoned transfers after eight seconds without a valid request. */
            try
            {
                using var release = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await ExchangeAsync(FlightLogProtocol.End, transferId, 0, release.Token).ConfigureAwait(false);
            }
            catch { }
            Volatile.Write(ref running, 0);
        }
    }

    private async Task<byte[]> ExchangeAsync(byte operation, uint id, uint sequence, CancellationToken ct,
        byte profile = FlightLogProtocol.CompactProfile)
    {
        uint expectedSequence = operation switch
        {
            FlightLogProtocol.Begin => FlightLogProtocol.BeginSequence,
            FlightLogProtocol.End => FlightLogProtocol.EndSequence,
            _ => sequence
        };
        byte[] request = FlightLogProtocol.BuildRequest(operation, id, sequence, profile);
        for (int attempt = 0; attempt < 6; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await send(request, ct).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ResponseTimeout);
            try
            {
                while (true)
                {
                    byte[] packet = await responses.Reader.ReadAsync(timeout.Token).ConfigureAwait(false);
                    if (FlightLogSession.U32(packet, 4) != id || FlightLogSession.U32(packet, 8) != expectedSequence) continue;
                    if (packet[1] != 0)
                        throw new InvalidOperationException(packet[1] switch
                        {
                            1 => "No diagnostic log is available. Update the flight-controller firmware for downloads before takeoff.",
                            2 => "The aircraft is still armed. DISARM before downloading.",
                            3 => "The log transfer expired or changed. Download again.",
                            _ => "The aircraft rejected the requested log chunk."
                        });
                    return packet;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        }
        throw new TimeoutException("No flight-log response after six attempts. Check the radio link and update both firmwares.");
    }
}

internal readonly record struct FlightLogDownloadProgress(int ReceivedBytes, int TotalBytes);
