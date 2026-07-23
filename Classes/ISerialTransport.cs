using System;
using System.Threading;
using System.Threading.Tasks;

namespace DroneLogger.Classes
{
    internal interface ISerialTransport : IDisposable
    {
        event EventHandler<string> LineReceived;
        event EventHandler ConnectionLost;
        bool IsOpen { get; }
        Task OpenAsync(string portName, CancellationToken ct);
        Task CloseAsync();
        Task WriteAsync(byte[] buffer, CancellationToken ct);
    }
}
