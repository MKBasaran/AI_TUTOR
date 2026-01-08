using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ServerCore.Logging;
using static ServerCore.Communication.ICommunicationManager;

namespace ServerCore.Communication.Serial;

/// <summary>
/// <inheritdoc cref="ICommunicationManager"/>
/// <br/>
/// This implementation works with the serial ports as the underlying communication protocol, with <see cref="SerialCommunicationChannel"/> as the <see cref="ICommunicationChannel"/> implementation.
/// </summary>
/// <remarks>
/// <para>
/// <inheritdoc cref="ICommunicationManager"/>
/// </para>
/// <para>
/// </para>
/// </remarks>
public sealed class SerialCommunicationManager : ICommunicationManager
{
    // We want to keep track of ports that have existed previously, since we only want to attempt connections to new ports
    private HashSet<string> trackedPorts = [];

    // This is used to ensure safe disposal of channels (even if the consumer fails to use it)
    private readonly Dictionary<string, ICommunicationChannel> openChannels = [];
    private readonly Dictionary<string, ICommunicationChannel> waitingChannels = [];

    /// <inheritdoc/>
    public CommunicationChannelEstablishedAction? CommunicationChannelEstablished { get; set; }

    /// <inheritdoc/>
    public CommunicationChannelLostAction? CommunicationChannelLost { get; set; }

    private CancellationTokenSource? cancellationTokenSource;
    private Task? updateTask;

    /// <inheritdoc/>
    public void Start()
    {
        cancellationTokenSource = new();
        updateTask = Task.Run(() => update(cancellationTokenSource.Token));
    }

    private async Task update(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                var delayTask = Task.Delay(1000, ct);

                // First update waiting channels
                foreach ((string port, var waitingChannel) in waitingChannels)
                {
                    switch (waitingChannel.Status)
                    {
                        case ConnectionStatus.Connected:
                            openChannels[port] = waitingChannel;
                            CommunicationChannelEstablished?.Invoke(waitingChannel);
                            waitingChannels.Remove(port);
                            break;

                        case ConnectionStatus.Failed:
                        case ConnectionStatus.Closed:
                            waitingChannel.Dispose();
                            waitingChannels.Remove(port);
                            break;
                    }
                }

                HashSet<string> availablePorts = [..getSerialPorts()];

                foreach (string p in availablePorts.Where(p => !trackedPorts.Contains(p)))
                {
                    _ = StandardLogs.RUNTIME_LOGGER.LogAsync($"Detected new serial connection {p}.");

                    SerialCommunicationChannel c = new(p);
                    waitingChannels[p] = c;
                    trackedPorts.Add(p);
                }

                foreach (string p in trackedPorts.Where(p => !availablePorts.Contains(p)))
                {
                    _ = StandardLogs.RUNTIME_LOGGER.LogAsync($"Serial connection {p} disconnected.");

                    if (!openChannels.TryGetValue(p, out var channel))
                        continue;

                    channel.Dispose();
                    openChannels.Remove(p);
                    CommunicationChannelLost?.Invoke(channel);
                }

                trackedPorts = availablePorts;

                await delayTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is an expected outcome, marking the end of this task.
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    private string[] getSerialPorts()
    {
        if (!OperatingSystem.IsWindows()) return SerialPort.GetPortNames();


        // Registry ports
        // On windows, the port may not be actually available. For some reason, the registry will continue to hold the portname long after the port has been closed.
        // The port is only removed on program termination.
        // Device manager will still show proper ports, so we can simply get the intersection between the two.
        using var searcher = new ManagementObjectSearcher("SELECT * FROM WIN32_SerialPort");

        string[] portnames = SerialPort.GetPortNames();
        var ports = searcher.Get().Cast<ManagementBaseObject>().ToList();

#pragma warning disable CA1416 // Required due to the false positive OS check within LINQ statements
        var tList = (from n in portnames
            join p in ports on n equals p["DeviceID"].ToString()
            select n).ToList();
#pragma warning restore CA1416

        return [.. tList];
    }

    /// <inheritdoc/>
    public void Stop()
    {
        if (cancellationTokenSource is null)
            return;

        Debug.Assert(cancellationTokenSource is not null);
        Debug.Assert(updateTask is not null);

        Task.WaitAll(cancellationTokenSource.CancelAsync(), updateTask);
    }

    private bool isDisposed;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed)
            return;

        Stop();
        isDisposed = true;
    }
}
