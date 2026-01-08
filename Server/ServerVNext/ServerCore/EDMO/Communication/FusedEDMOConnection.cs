using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ServerCore.EDMO.Communication.Packets;
using static ServerCore.EDMO.Communication.EDMOConnection;

namespace ServerCore.EDMO.Communication;

/// <summary>
/// This is a wrapper connection that holds multiple <see cref="EDMOConnection"/>s connected to the same Robot. It provides seamless switching when connections are established/lost.
/// </summary>
public class FusedEDMOConnection
{
    private readonly List<EDMOConnection> connections = [];

    /// <inheritdoc cref="EDMOConnection.OscillationDataReceived"/>
    public OscillatorDataPacketHandler? OscillationDataReceived { get; set; }

    /// <inheritdoc cref="EDMOConnection.ImuDataReceived"/>
    public ImuDataPacketHandler? ImuDataReceived { get; set; }

    /// <inheritdoc cref="EDMOConnection.TimeReceived"/>
    public TimePacketHandler? TimeReceived { get; set; }

    /// <inheritdoc cref="EDMOConnection.UnknownPacketReceived"/>
    public UnknownPacketHandler? UnknownPacketReceived { get; set; }

    private EDMOConnection? currentConnection { get; set; } = null!;

    /// <inheritdoc cref="EDMOConnection.IsLocked"/>
    public bool IsLocked => connections.FirstOrDefault()?.IsLocked ?? false;

    /// <inheritdoc cref="EDMOConnection.LockStateChanged"/>
    public Action? LockStateChanged { get; set; }

    /// <inheritdoc cref="EDMOConnection.Identifier"/>
    public string Identifier { get; private set; }

    /// <inheritdoc cref="EDMOConnection.ArmColourHues"/>
    /// <remarks>
    /// <inheritdoc cref="EDMOConnection.ArmColourHues"/>
    /// <br/>
    /// If no connection is associated with this fused connection, this will return an empty array.
    /// </remarks>
    public ushort[] ArmColourHues => connections.FirstOrDefault()?.ArmColourHues ?? [];

    /// <inheritdoc cref="EDMOConnection.OscillatorCount"/>
    /// <remarks>
    /// <inheritdoc cref="EDMOConnection.ArmColourHues"/>
    /// <br/>
    /// If no connection is associated with this fused connection, this will return <c>0</c>
    /// </remarks>
    public ushort OscillatorCount => connections.FirstOrDefault()?.OscillatorCount ?? 0;

    /// <inheritdoc cref="FusedEDMOConnection"/>
    /// <param name="identifier">The identifier for this fused connection.</param>
    public FusedEDMOConnection(string identifier)
    {
        Identifier = identifier;
    }

    /// <summary>
    /// Add a connection to this fused connection.
    /// </summary>
    /// <param name="connection">The connection to be added</param>
    /// <remarks>
    /// Adding the same connection multiple times may cause undefined behaviour.
    /// </remarks>
    public void AddConnection(EDMOConnection connection)
    {
        connections.Add(connection);
        if (currentConnection is not null)
            return;

        currentConnection = connection;
        bindHandlers();
    }


    /// <summary>
    /// Removes a connection from this fused connection.
    /// </summary>
    /// <param name="connection">The connection to be removes</param>
    /// <remarks>
    /// Adding the same connection multiple times may cause undefined behaviour.
    /// </remarks>
    public void RemoveConnection(EDMOConnection connection)
    {
        connections.Remove(connection);

        if (connection != currentConnection)
            return;

        unbindHandlers();

        currentConnection = connections.FirstOrDefault();

        bindHandlers();
    }

    private void bindHandlers()
    {
        if (currentConnection is null)
            return;

        currentConnection.LockStateChanged += lockStateChangedEventHandler;
        currentConnection.OscillationDataReceived += oscillationDataReceivedEventHandler;
        currentConnection.ImuDataReceived += imuDataReceivedEventHandler;
        currentConnection.UnknownPacketReceived += unknownPacketReceivedEventHandler;
        currentConnection.TimeReceived += timeReceivedEventHandler;
    }

    private void unbindHandlers()
    {
        if (currentConnection is null) return;

        currentConnection.LockStateChanged -= lockStateChangedEventHandler;
        currentConnection.OscillationDataReceived -= oscillationDataReceivedEventHandler;
        currentConnection.ImuDataReceived -= imuDataReceivedEventHandler;
        currentConnection.UnknownPacketReceived -= unknownPacketReceivedEventHandler;
        currentConnection.TimeReceived -= timeReceivedEventHandler;
    }

    /// <inheritdoc cref="EDMOConnection.Write{T}"/>
    public void Write<T>(T data) where T : unmanaged
    {
        currentConnection?.Write(data);
    }

    /// <inheritdoc cref="EDMOConnection.WriteAsync{T}"/>
    public async Task WriteAsync<T>(T data) where T : unmanaged
    {
        await (currentConnection?.WriteAsync(data) ?? Task.CompletedTask);
    }

    /// <summary>
    /// The number of connections tracked by this fused connection.
    /// </summary>
    public int NumberConnections => connections.Count;

    private void oscillationDataReceivedEventHandler(EDMOConnection c, in OscillatorDataPacket packet) =>
        OscillationDataReceived?.Invoke(c, in packet);

    private void imuDataReceivedEventHandler(EDMOConnection c, in IMUDataPacket packet) =>
        ImuDataReceived?.Invoke(c, in packet);

    private void timeReceivedEventHandler(EDMOConnection c, in TimePacket packet) =>
        TimeReceived?.Invoke(c, in packet);

    private void unknownPacketReceivedEventHandler(EDMOConnection c, ReadOnlySpan<byte> data) =>
        UnknownPacketReceived?.Invoke(c, data);

    private void lockStateChangedEventHandler() => LockStateChanged?.Invoke();
}
