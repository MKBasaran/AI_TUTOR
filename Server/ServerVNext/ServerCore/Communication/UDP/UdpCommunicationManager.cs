using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static ServerCore.Communication.ICommunicationManager;

namespace ServerCore.Communication.UDP;

/// <summary>
/// <inheritdoc cref="ICommunicationManager"/>
/// <br/>
/// This implementation works with UDP broadcasts as the underlying communication protocol, with <see cref="UdpCommunicationChannel"/> as the <see cref="ICommunicationChannel"/> implementation.
///
/// All supported network interfaces will participate in the search procedure. The available network interfaces is queried during construction of the communication manager. It will not acknowledge new interfaces after than point.
/// If a network interface is lost, the communication manager will simply discard it. Reconnection is not supported.
/// </summary>
/// <remarks>
/// <inheritdoc cref="ICommunicationManager"/>
///
/// </remarks>
public sealed class UdpCommunicationManager : ICommunicationManager
{
    /// <inheritdoc/>
    public CommunicationChannelEstablishedAction? CommunicationChannelEstablished { get; set; }

    /// <inheritdoc/>
    public CommunicationChannelLostAction? CommunicationChannelLost { get; set; }

    private readonly Dictionary<IPAddress, ICommunicationChannel> activeCommunicationChannels = [];

    private readonly UdpClient client;

    private readonly List<IPEndPoint> addresses;

    /// <summary>
    /// The message sent when polling for listeners. If set to <c>null</c>, polling does not occur, and the manager instance will only listen for external messages.
    /// </summary>
    public string? PollMessage { get; init; }

    /// <inheritdoc cref="UdpCommunicationManager"/>
    /// <param name="port">The port number to communicate through</param>
    public UdpCommunicationManager(int port)
    {
        client = new UdpClient(new IPEndPoint(IPAddress.Any, 0))
        {
            EnableBroadcast = true
        };
        addresses = [..getBroadcastEndpoints(port)];
    }

    private static IPAddress getBroadcastIP(IPAddress address, IPAddress mask)
    {
        uint ipAddress = BitConverter.ToUInt32(address.GetAddressBytes(), 0);
        uint ipMaskV4 = BitConverter.ToUInt32(mask.GetAddressBytes(), 0);
        uint broadCastIpAddress = ipAddress | ~ipMaskV4;

        return new IPAddress(BitConverter.GetBytes(broadCastIpAddress));
    }

    private static IEnumerable<IPEndPoint> getBroadcastEndpoints(int port)
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(i => i.Supports(NetworkInterfaceComponent.IPv4) &&
                        i.NetworkInterfaceType is not NetworkInterfaceType.Loopback)
            .SelectMany(i => i.GetIPProperties().UnicastAddresses)
            .Where(u => u.Address.AddressFamily is AddressFamily.InterNetwork)
            .Select(u => new IPEndPoint(getBroadcastIP(u.Address, u.IPv4Mask), port));
    }

    private CancellationTokenSource? cancellationTokenSource;
    private Task? updateTask;
    private Task? listenTask;

    /// <inheritdoc/>
    public void Start()
    {
        cancellationTokenSource = new CancellationTokenSource();
        updateTask = Task.Run(() => updateLoop(cancellationTokenSource.Token));
        listenTask = Task.Run(() => listenLoop(cancellationTokenSource.Token));
    }

    /// <inheritdoc/>
    public void Stop()
    {
        if (cancellationTokenSource is null)
            return;

        Debug.Assert(cancellationTokenSource is not null);
        Debug.Assert(updateTask is not null);
        Debug.Assert(listenTask is not null);

        try
        {
            Task.WaitAll([cancellationTokenSource.CancelAsync(), updateTask, listenTask]);
        }
        catch
        {
            //Ignored
        }

        foreach (var kvp in activeCommunicationChannels)
        {
            CommunicationChannelLost?.Invoke(kvp.Value);
            kvp.Value.Close();
        }

        activeCommunicationChannels.Clear();
        client.Close();
    }

    private async Task updateLoop(CancellationToken ct)
    {
        if (PollMessage is null)
            return;

        byte[] bytes = Encoding.ASCII.GetBytes(PollMessage);

        try
        {
            while (true)
            {
                if (addresses.Count == 0)
                    break;

                var delayTask = Task.Delay(1000, ct);
                for (int i = 0; i < addresses.Count; ++i)
                {
                    var ipAddress = addresses[i];

                    try
                    {
                        await client.SendAsync(bytes, ipAddress, ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        Console.WriteLine("err");
                        addresses.RemoveAt(i);
                        --i;
                    }
                }

                // Let's also update the list of active communications in the meantime
                foreach (var (ip, communicationChannel) in activeCommunicationChannels)
                {
                    if (communicationChannel.Status != ConnectionStatus.Closed)
                        continue;

                    activeCommunicationChannels.Remove(ip);
                    CommunicationChannelLost?.Invoke(communicationChannel);
                }

                await delayTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is an expected outcome, marking the end of this task.
        }
    }

    private async Task listenLoop(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                var result = await client.ReceiveAsync(ct);
                IPAddress address = result.RemoteEndPoint.Address;

                if (!activeCommunicationChannels.TryGetValue(address, out var channel))
                {
                    channel = activeCommunicationChannels[address] =
                        new UdpCommunicationChannel(client, result.RemoteEndPoint)
                            { InactivityTimeout = TimeSpan.FromSeconds(10) };
                    CommunicationChannelEstablished?.Invoke(channel);
                }

                channel.DataReceived?.Invoke(result.Buffer);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is an expected outcome, marking the end of this task.
        }
    }

    private bool isDisposed;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed) return;

        isDisposed = true;
        Stop();
    }
}
