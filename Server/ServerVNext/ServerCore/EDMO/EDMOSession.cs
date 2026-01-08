using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ServerCore.EDMO.Communication;
using ServerCore.EDMO.Communication.Commands;
using ServerCore.EDMO.Communication.Packets;
using ServerCore.EDMO.Plugins;
using ServerCore.EDMO.Plugins.Loaders;
using ServerCore.Logging;

namespace ServerCore.EDMO;

/// <summary>
/// Represents a single EDMO study session, managing both the users and the device.
///
/// <br/>
/// The session will run a background thread to update the connected robot.
/// </summary>
public partial class EDMOSession
{
    private readonly EDMOSessionManager manager;
    private FusedEDMOConnection? deviceConnection;

    internal readonly Dictionary<int, ControlContext> ConnectedUsers = [];

    private PriorityQueue<int, int> availableSlots = new();

    internal readonly EDMOPlugin[] Plugins = [];

    /// <summary>
    /// Whether this session is full.
    /// </summary>
    public bool IsFull => availableSlots.Count == 0;

    /// <summary>
    /// Whether this session is ending/ended.
    /// </summary>
    public bool IsClosed { get; private set; }

    /// <summary>
    /// Whether there is an active connection to an EDMO robot
    /// </summary>
    public bool HasDevice => deviceConnection is not null;

    internal OscillatorParams[] OscillatorParams = [];


    private uint lastTime;

    /// <summary>
    /// The Identifier associated with this session, usually the same as <see cref="EDMOConnection.Identifier"/>.
    /// </summary>
    public readonly string Identifier;

    private ushort oscillatorCount;
    private ushort[] hues = [];

    /// <summary>
    /// The canonical storage directory of the session. Can be used by plugins to store their own data related to the session
    /// </summary>
    /// <remarks>
    /// This <see cref="DirectoryInfo"/> always represents the full <i>local</i> directory. If only a relative path is requested (perhaps for storage mirroring), use <see cref="RelativeSessionStorageDirectory"/> instead.
    /// </remarks>
    public readonly DirectoryInfo SessionStorageDirectory;

    /// <summary>
    /// The relative path of the storage directory.
    /// </summary>
    public readonly string RelativeSessionStorageDirectory;

    /// <inheritdoc cref="EDMOSession"/>
    /// <param name="manager">The hosting session manager, used internally</param>
    /// <param name="identifier">The identifier to be associated with this session</param>
    /// <param name="pluginLoader">The plugin loader to be used to create plugins. If null, no plugins will be loaded</param>
    /// <param name="connection">The connection to the device. If provided, will call <see cref="BindConnection"/> after plugin loads.</param>
    public EDMOSession(EDMOSessionManager manager, string identifier,
        IPluginLoader? pluginLoader = null, FusedEDMOConnection? connection = null)
    {
        DateTime dateTime = DateTime.Now;
        RelativeSessionStorageDirectory = $"Sessions/{dateTime:yyyyMMdd}/{identifier}/{dateTime:HHmmss}";

        SessionStorageDirectory =
            new DirectoryInfo($"{StandardLogs.DEFAULT_LOG_DIRECTORY}/{RelativeSessionStorageDirectory}");

        this.manager = manager;
        Identifier = identifier;

        updateTask = Task.Run(updateLoop);

        if (pluginLoader is not null)
            Plugins = [new LoggingPlugin(this), ..pluginLoader.CreatePluginsFor(this)];

        foreach (var plugin in Plugins)
            plugin.SessionStarted();

        if (connection is not null)
            BindConnection(connection);
    }

    /// <summary>
    /// Binds a connection to this session, and registers the event handlers.
    /// </summary>
    /// <param name="connection">The session to be bound</param>
    public void BindConnection(FusedEDMOConnection connection)
    {
        if (deviceConnection == connection)
            return;

        deviceConnection = connection;

        if (deviceConnection.OscillatorCount > oscillatorCount)
        {
            oscillatorCount = deviceConnection.OscillatorCount;

            var newOscillatorParams = new OscillatorParams[oscillatorCount];
            Array.Fill(newOscillatorParams, default_osc_params);

            OscillatorParams.CopyTo(newOscillatorParams, 0);
            OscillatorParams = newOscillatorParams;

            availableSlots.Clear();
            availableSlots.EnqueueRange(Enumerable.Range(0, oscillatorCount).Except(ConnectedUsers.Keys)
                .Select(i => (i, i)));

            hues = new ushort[oscillatorCount];
        }

        deviceConnection.ArmColourHues.CopyTo(hues, 0);

        signalSessionStart();

        deviceConnection.ImuDataReceived += imuDataReceived;
        deviceConnection.OscillationDataReceived += oscillatorDataReceived;
        deviceConnection.TimeReceived += timeReceived;

        updateCancellationToken = CancellationTokenSource.CreateLinkedTokenSource(sessionCancellationToken.Token);
        updateTask = Task.Run(updateLoop);
    }

    /// <summary>
    /// Unregisters event handlers, and unbinds the currently bound connection.
    /// </summary>
    public void UnbindConnection()
    {
        if (deviceConnection is null)
            return;

        updateCancellationToken.Cancel();
        updateTask = null;

        // Conservatively unbind callbacks
        deviceConnection.ImuDataReceived -= imuDataReceived;
        deviceConnection.OscillationDataReceived -= oscillatorDataReceived;
        deviceConnection.TimeReceived -= timeReceived;

        deviceConnection = null;
    }

    private void signalSessionStart()
    {
        if (deviceConnection is null)
            return;

        for (byte i = 0; i < deviceConnection.OscillatorCount; ++i)
            deviceConnection.Write(new UpdateOscillatorCommand(i, OscillatorParams[i]));

        deviceConnection.Write(new SessionStartCommand(lastTime));
    }

    private void imuDataReceived(EDMOConnection _, in IMUDataPacket imuData)
    {
        foreach (EDMOPlugin edmoPlugin in Plugins)
            edmoPlugin.ImuDataReceived(imuData);
    }

    private void oscillatorDataReceived(EDMOConnection _, in OscillatorDataPacket oscillatorDataPacket)
    {
        foreach (EDMOPlugin edmoPlugin in Plugins)
            edmoPlugin.OscillatorDataReceived(oscillatorDataPacket);

        ConnectedUsers.GetValueOrDefault(oscillatorDataPacket.OscillatorIndex)
            ?.OscillatorDataReceived
            ?.Invoke(oscillatorDataPacket);
    }

    private void timeReceived(EDMOConnection c, in TimePacket timePacket)
    {
        lastTime = timePacket.Time;
    }

    private readonly SemaphoreSlim sessionContextSemaphore = new(1, 1);

    /// <summary>
    /// Creates a context that exposes control functionality, providing a control ID, methods to manipulate the assigned oscillator, and methods to get information about the session.
    /// <br/>
    /// Plugins and other users will be notified.
    /// </summary>
    /// <param name="userName">The name of the controller</param>
    /// <returns>A control context</returns>
    /// <exception cref="InvalidOperationException">
    /// Session has been closed.
    /// <br/>
    /// -- or --
    /// <br/>
    /// The session is full.
    /// </exception>
    public ControlContext CreateContext(string userName)
    {
        sessionContextSemaphore.Wait();
        if (IsClosed)
        {
            throw new InvalidOperationException("Session has closed. Please try again.");
        }

        if (IsFull)
        {
            sessionContextSemaphore.Release();
            throw new InvalidOperationException("Can't add another user to a full session.");
        }

        int id = availableSlots.Dequeue();
        var ctx = ConnectedUsers[id] = new ControlContext(this, id, userName);

        foreach (var controlCtx in ConnectedUsers.Values)
            controlCtx.PlayerListUpdated?.Invoke();

        sessionContextSemaphore.Release();

        foreach (var edmoPlugin in Plugins)
            edmoPlugin.UserJoined(id, userName);

        return ctx;
    }

    private void closeContext(ControlContext user)
    {
        sessionContextSemaphore.Wait();

        foreach (var edmoPlugin in Plugins)
            edmoPlugin.UserLeft(user.Index, user.ControllerIdentifier);

        availableSlots.Enqueue(user.Index, user.Index);
        ConnectedUsers.Remove(user.Index);

        foreach (var controlCtx in ConnectedUsers.Values)
            controlCtx.PlayerListUpdated?.Invoke();

        if (ConnectedUsers.Count == 0)
            Close();

        sessionContextSemaphore.Release();
        // Inform users of the session manager that a slot may be available
        manager.AvailableSessionsUpdated?.Invoke();
    }

    private readonly CancellationTokenSource sessionCancellationToken = new();
    private CancellationTokenSource updateCancellationToken = new();
    private Task? updateTask;

    /// <summary>
    /// We do this separately as an attempt to debounce state changes to the EDMO hardware.
    /// </summary>
    private async Task updateLoop()
    {
        while (!updateCancellationToken.IsCancellationRequested)
        {
            if (deviceConnection is not null)
            {
                foreach (EDMOPlugin edmoPlugin in Plugins)
                    edmoPlugin.Update();

                for (byte i = 0; i < OscillatorParams.Length; ++i)
                    await deviceConnection.WriteAsync(new UpdateOscillatorCommand(i, OscillatorParams[i]))
                        .ConfigureAwait(false);
            }

            // This is short enough that we really don't need to pass and handle cancellation on this
            await Task.Delay(50, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static readonly OscillatorParams default_osc_params = new(0, 0, 90, 0);

    private void resetHardwareState()
    {
        if (deviceConnection is null)
            return;

        for (byte i = 0; i < OscillatorParams.Length; ++i)
            deviceConnection.Write(new UpdateOscillatorCommand(i, default_osc_params));
    }

    /// <summary>
    /// Closes this session, resetting the robot's state, cleans up plugins, and informs <see cref="EDMOSessionManager"/> of the closure.
    /// </summary>
    public void Close()
    {
        IsClosed = true;
        Task.WaitAll(sessionCancellationToken.CancelAsync(), updateTask ?? Task.CompletedTask);
        resetHardwareState();

        deviceConnection?.Write(new SessionEndCommand());

        UnbindConnection();
        manager.UnregisterSession(Identifier);

        foreach (var edmoPlugin in Plugins)
        {
            edmoPlugin.SessionEnded();
            edmoPlugin.Dispose();
        }
    }
}
