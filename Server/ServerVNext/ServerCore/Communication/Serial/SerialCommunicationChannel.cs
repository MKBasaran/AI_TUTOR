using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using ServerCore.Logging;
using static ServerCore.Communication.ICommunicationChannel;

namespace ServerCore.Communication.Serial;

/// <summary>
/// A <see cref="ICommunicationChannel"/> where the underlying connection is based on Serial communication.
/// </summary>
/// <remarks>
/// Receiving data from serial ports are done in a separate thread.
/// </remarks>
public sealed class SerialCommunicationChannel : ICommunicationChannel
{
    private readonly SerialPort port;

    private Stream? baseStream => port.IsOpen ? port.BaseStream : null;

    private readonly CancellationTokenSource cts = new CancellationTokenSource();
    private Task activeTask;

    /// <summary>
    /// <inheritdoc cref="SerialCommunicationChannel"/>
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="SerialCommunicationChannel"/>
    /// </remarks>
    /// <param name="port">The name of the Serial port to connect to, in preferred format for the OS</param>
    /// <param name="connectionTimeout">The upper time limit (in milliseconds) the channel should wait for a connection to establish before failing.</param>
    public SerialCommunicationChannel(string port, int connectionTimeout = 3000)
    {
        this.port = new SerialPort
        {
            PortName = port,
            BaudRate = 9600,

            // This prevents the arduino from performing a reset
            DtrEnable = true,
        };

        activeTask = Task.Run(() => attemptConnection(connectionTimeout, cts.Token));
    }

    /// <inheritdoc/>
    public ConnectionStatus Status { get; private set; }

    private async Task attemptConnection(int timeout, CancellationToken ct)
    {
        CancellationTokenSource timeoutCts = new();
        CancellationTokenSource cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var token = cts2.Token;

        Status = ConnectionStatus.Waiting;
        timeoutCts.CancelAfter(timeout);

        try
        {
            while (!port.IsOpen)
            {
                try
                {
                    port.Open();
                }
                // Due to quirks in the Serial port opening, there can be recoverable exceptions.
                catch (UnauthorizedAccessException)
                {
                    await StandardLogs.RUNTIME_LOGGER.LogAsync(
                        $"Failed to establish connection to {port.PortName} due to pre-existing lock. This is not an indication of failure, and may be recoverable.");
                }

                await Task.Delay(500, token);
                token.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException)
        {
            await StandardLogs.RUNTIME_LOGGER.LogAsync(
                $"Failed to establish connection to {port.PortName}. Unable to obtain access to port.",
                LogLevel.Info);
            Status = ConnectionStatus.Failed;
            return;
        }

        Status = ConnectionStatus.Connected;

        // The friendly name is used, as by this time there may be a better way to identify
        await StandardLogs.RUNTIME_LOGGER.LogAsync($"Successfully established connection to {port.PortName}.",
            LogLevel.Info);

        connectionEstablished();
    }

    private async Task updateTask(CancellationToken ct)
    {
        byte[] bytes = ArrayPool<byte>.Shared.Rent(512);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!port.IsOpen)
                    return;

                Debug.Assert(baseStream is not null);

                int bytesRead = await baseStream.ReadAsync(bytes, ct);

                if (bytesRead > 0)
                    DataReceived?.Invoke(bytes.AsSpan(0, bytesRead));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    private void connectionEstablished()
    {
        activeTask = Task.Run(() => updateTask(cts.Token));
    }

    /// <inheritdoc/>
    public DataReceivedAction? DataReceived { get; set; }

    /// <inheritdoc/>
    public async Task WriteAsync(ReadOnlyMemory<byte> data) =>
        await (baseStream?.WriteAsync(data) ?? ValueTask.CompletedTask);

    /// <inheritdoc/>
    public void Write(ReadOnlySpan<byte> data) => baseStream?.Write(data);

    /// <inheritdoc/>
    public void Close()
    {
        try
        {
            Task.WaitAll(cts.CancelAsync(), activeTask);
        }
        catch
        {
            //Ignore any exceptions occuring during cancellation
        }

        // When yanking the USB serial, SerialPort.Close() may deadlock if something is still attempting to write to the stream.
        // (Depending on the USB serial emulator, the OS may not know this happened)
        // Let's close the underlying stream directly.
        baseStream?.Close();

        Status = ConnectionStatus.Closed;
    }

    private bool disposed;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
            return;

        Close();
        port.Dispose();
        disposed = true;
    }
}
