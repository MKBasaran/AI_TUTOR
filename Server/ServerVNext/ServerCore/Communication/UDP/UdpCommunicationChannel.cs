using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using ServerCore.Logging;
using static ServerCore.Communication.ICommunicationChannel;

namespace ServerCore.Communication.UDP;

/// <summary>
/// A <see cref="ICommunicationChannel"/> where the underlying connection is based on UDP communication.
/// </summary>
///
/// <remarks>
/// <para>
/// This channel does not manage its own <see cref="UdpClient"/>, but uses the same <see cref="UdpClient"/> as the <see cref="UdpCommunicationManager"/> this channel was created from.
/// The <see cref="UdpCommunicationManager"/> is responsible for passing received messages to the appropriate channel instance.
/// </para>
/// <para>
/// Closing the originating <see cref="UdpCommunicationManager"/> will also stop this channel from being updated.
/// </para>
/// </remarks>
public sealed class UdpCommunicationChannel : ICommunicationChannel
{
    private ConnectionStatus actualStatus;

    /// <inheritdoc/>
    public ConnectionStatus Status
    {
        get
        {
            TimeSpan timeWithoutMessage = DateTime.Now - lastMessageTime;

            return timeWithoutMessage >= InactivityTimeout ? ConnectionStatus.Closed : actualStatus;
        }
    }

    /// <inheritdoc/>
    public DataReceivedAction? DataReceived { get; set; }

    /// <summary>
    /// The amount of time without receiving a message before the connection is considered closed.
    /// </summary>
    /// <remarks>
    /// Default timeout is set to 10 seconds.
    /// </remarks>
    public TimeSpan InactivityTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <inheritdoc cref="UdpCommunicationChannel"/>
    /// <param name="client">The UdpClient this communication channel will write to</param>
    /// <param name="endpoint">The remote endpoint of all communications through this channel</param>
    public UdpCommunicationChannel(UdpClient client, IPEndPoint endpoint)
    {
        transport = client;
        this.endpoint = endpoint;

        StandardLogs.RUNTIME_LOGGER.Log($"Created UDP Connection {endpoint}");

        actualStatus = ConnectionStatus.Connected;

        DataReceived += _ => lastMessageTime = DateTime.Now;
    }

    private readonly UdpClient transport;
    private readonly IPEndPoint endpoint;

    private DateTime lastMessageTime = DateTime.Now;

    /// <inheritdoc/>
    public void Close()
    {
        actualStatus = ConnectionStatus.Closed;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Close();
    }

    /// <inheritdoc/>
    public async Task WriteAsync(ReadOnlyMemory<byte> data)
    {
        if (actualStatus is ConnectionStatus.Closed)
            return;

        await transport.SendAsync(data, endpoint);
    }

    /// <inheritdoc/>
    public void Write(ReadOnlySpan<byte> data)
    {
        if (actualStatus is ConnectionStatus.Closed)
            return;

        transport.Send(data, endpoint);
    }
}
