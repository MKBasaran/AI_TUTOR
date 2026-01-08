using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ServerCore.Communication.UDP;
using ServerCore.Communication;
using ServerCore.Communication.Serial;
using ServerCore.EDMO.Communication.Commands;
using ServerCore.Logging;

namespace ServerCore.EDMO.Communication;

/// <summary>
/// A high level management class to search for and manage <see cref="EDMOConnection"/>s.
/// <br/>
/// This manager is also responsible for merging multiple communication channels to the same EDMO robot into a composite variant of <see cref="EDMOConnection"/>, known as <see cref="FusedEDMOConnection"/>.
/// <br/>
/// Connections are managed in a background thread.
/// </summary>
/// <remarks>
/// <para>
/// Currently, this manager attempts to establish communication to EDMO robots via <see cref="SerialCommunicationManager"/> and <see cref="UdpCommunicationManager"/><c>{PollMessage="ED\0MO", port=2121}</c>.
/// </para>
/// <para>
/// <see cref="EDMOConnection"/>s are fused together based on their identifier. There is an expectation that every unique EDMO robot would have a unique identifier. Having multiple EDMO robots with the same identifier will result in undefined behaviour.
/// </para>
///
/// <para>
/// Serial communication will be prioritised over UDP connection if available. Transitions between the two will be silent.
/// </para>
/// </remarks>
public class EDMOConnectionManager : IDisposable
{
    private readonly ICommunicationManager[] communicationManagers =
    [
        new SerialCommunicationManager(),
        new UdpCommunicationManager(2121)
        {
            PollMessage = Encoding.ASCII.GetString([
                .."ED"u8, ..IdentificationCommand.BYTES, .."MO"u8
            ])
        }
    ];

    private readonly Dictionary<string, FusedEDMOConnection> openConnections = [];

    private readonly List<EDMOConnection> waitingConnections = [];
    private readonly SemaphoreSlim listSemaphore = new SemaphoreSlim(1, 1);

    private readonly Dictionary<ICommunicationChannel, EDMOConnection> connectionMap = [];

    /// <summary>
    /// An event that is raised whenever a new EDMO robot is connected;
    /// </summary>
    /// <remarks>
    /// This is only fired when an EDMO first connects. This does not get raised if another connection is established to the same EDMO robot.
    /// </remarks>
    public EDMOConnectionEstablishedHandler? EDMOConnectionEstablished { get; set; }

    /// <summary>
    /// An event that is raised whenever a new EDMO robot is connected;
    /// </summary>
    /// <remarks>
    /// This is only fired when the last connection of EDMO is lost. This does not get raised if there is still another connection to the same EDMO robot.
    /// </remarks>
    public EDMOConnectionLostHandler? EDMOConnectionLost { get; set; }

    private CancellationTokenSource cancellationTokenSource = null!;

    private Task? updateTask;

    /// <inheritdoc cref="EDMOConnectionManager"/>
    public EDMOConnectionManager()
    {
        foreach (var cm in communicationManagers)
        {
            cm.CommunicationChannelEstablished += OnCommunicationChannelEstablished;
            cm.CommunicationChannelLost += OnCommunicationChannelLost;
        }
    }

    private async Task update(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await listSemaphore.WaitAsync(ct);

            // Evaluate waiting connections
            for (int i = 0; i < waitingConnections.Count; ++i)
            {
                var connection = waitingConnections[i];

                bool shouldRemove = false;

                switch (connection.Status)
                {
                    case ConnectionStatus.Connected:
                        addToFusedConnection(connection);
                        shouldRemove = true;
                        break;
                    case ConnectionStatus.Failed:
                    case ConnectionStatus.Closed:
                        shouldRemove = true;
                        break;
                }

                if (!shouldRemove) continue;

                waitingConnections.RemoveAt(i);
                --i;
            }

            listSemaphore.Release();

            await Task.Delay(1000, CancellationToken.None);
        }
    }


    private void addToFusedConnection(EDMOConnection connection)
    {
        Debug.Assert(!string.IsNullOrWhiteSpace(connection.Identifier));

        if (!openConnections.TryGetValue(connection.Identifier, out FusedEDMOConnection? fusedConnection))
        {
            fusedConnection = openConnections[connection.Identifier] = new FusedEDMOConnection(connection.Identifier);
            StandardLogs.RUNTIME_LOGGER.Log($"Opened fused EDMO connection for identifier {connection.Identifier}");
            fusedConnection.AddConnection(connection);
            EDMOConnectionEstablished?.Invoke(fusedConnection);
            return;
        }

        StandardLogs.RUNTIME_LOGGER.Log($"Added EDMO connection for identifier {connection.Identifier}");
        fusedConnection.AddConnection(connection);
    }


    private void OnCommunicationChannelEstablished(ICommunicationChannel channel)
    {
        var edmoConnection = new EDMOConnection(channel);
        connectionMap[channel] = edmoConnection;

        listSemaphore.Wait();

        waitingConnections.Add(edmoConnection);
        listSemaphore.Release();
    }

    private void OnCommunicationChannelLost(ICommunicationChannel channel)
    {
        if (!connectionMap.TryGetValue(channel, out var edmoConnection))
            return;

        waitingConnections.Remove(edmoConnection);
        connectionMap.Remove(channel);

        // We never considered this channel valid anyway
        if (edmoConnection.Identifier is null) return;

        var fusedConnection = openConnections[edmoConnection.Identifier];
        fusedConnection.RemoveConnection(edmoConnection);

        StandardLogs.RUNTIME_LOGGER.Log(
            $"Removed EDMO connection for identifier {edmoConnection.Identifier}. Remaining connections: {fusedConnection.NumberConnections}.");

        if (fusedConnection.NumberConnections != 0) return;

        StandardLogs.RUNTIME_LOGGER.Log($"Removing FusedEDMOConnection for identifier {edmoConnection.Identifier}");
        openConnections.Remove(edmoConnection.Identifier);
        EDMOConnectionLost?.Invoke(fusedConnection);
    }

    /// <summary>
    /// <inheritdoc cref="ICommunicationManager.Start"/>
    /// </summary>
    public void Start()
    {
        foreach (var cm in communicationManagers)
            cm.Start();

        cancellationTokenSource = new CancellationTokenSource();
        updateTask = Task.Run(() => update(cancellationTokenSource.Token));
    }

    /// <summary>
    /// <inheritdoc cref="ICommunicationManager.Stop"/>
    /// </summary>
    public void Stop()
    {
        foreach (var cm in communicationManagers)
            cm.Stop();

        if (updateTask is not null)
            return;

        Debug.Assert(updateTask is not null);

        Task.WaitAll(cancellationTokenSource.CancelAsync(), updateTask);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var communicationManager in communicationManagers)
            communicationManager.Dispose();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Describes a handler for responding to EDMO connections
    /// </summary>
    public delegate void EDMOConnectionEstablishedHandler(FusedEDMOConnection connection);

    /// <summary>
    /// Describes a handler for responding to EDMO disconnections.
    /// </summary>
    public delegate void EDMOConnectionLostHandler(FusedEDMOConnection connection);
}
