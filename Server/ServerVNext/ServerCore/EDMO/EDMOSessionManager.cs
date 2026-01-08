using System;
using System.Collections.Generic;
using System.Threading;
using ServerCore.EDMO.Communication;
using ServerCore.EDMO.Plugins;
using ServerCore.EDMO.Plugins.Loaders;

namespace ServerCore.EDMO;

/// <summary>
/// A top level manager of <see cref="EDMOSession"/>. This manager initiates an <see cref="EDMOConnectionManager"/>, and keeps track of candidate and active sessions based on available EDMO robots.
/// <br/>
/// Methods are also provided to connect to sessions, obtaining the appropriate <see cref="EDMOSession.ControlContext"/>.
/// </summary>
public class EDMOSessionManager
{
    private readonly EDMOConnectionManager connectionManager = new();

    private readonly Dictionary<string, EDMOSession> activeSessions = [];
    private readonly Dictionary<string, FusedEDMOConnection> candidateSessions = [];

    /// <summary>
    /// An event that is raised when the list of available sessions are updated. Used so consumers can revalidate.
    /// </summary>
    public Action? AvailableSessionsUpdated;

    /// <summary>
    /// The plugin loader to be used during construction of all sessions.
    /// </summary>
    public IPluginLoader? SessionPluginLoader { get; init; }

    /// <summary>
    /// The list of sessions that can be connected to.
    /// </summary>
    public IEnumerable<string> AvailableSessions
    {
        get
        {
            foreach (var candidate in candidateSessions)
            {
                if (!activeSessions.TryGetValue(candidate.Key, out var existingSession))
                {
                    if (candidate.Value.IsLocked)
                        continue;

                    yield return candidate.Key;
                    continue;
                }

                if (existingSession.IsFull)
                    continue;

                if (!existingSession.HasDevice)
                    continue;

                yield return candidate.Key;
            }
        }
    }

    /// <summary>
    /// Starts this session manager.
    /// </summary>
    public void Start()
    {
        connectionManager.EDMOConnectionEstablished += onConnectionEstablished;
        connectionManager.EDMOConnectionLost += onConnectionLost;
        connectionManager.Start();
    }

    /// <summary>
    /// Stops this session manager, closing all <see cref="EDMOSession"/> created by this manager.
    /// </summary>
    public void Stop()
    {
        connectionManager.Stop();
        connectionManager.EDMOConnectionEstablished -= onConnectionEstablished;
        connectionManager.EDMOConnectionLost -= onConnectionLost;

        foreach (var activeSession in activeSessions)
            activeSession.Value.Close();

        activeSessions.Clear();
    }

    private void onConnectionEstablished(FusedEDMOConnection connection)
    {
        if (activeSessions.TryGetValue(connection.Identifier, out var session))
            session.BindConnection(connection);

        candidateSessions[connection.Identifier] = connection;
        connection.LockStateChanged += availableSessionsUpdated;
        availableSessionsUpdated();
    }

    private void onConnectionLost(FusedEDMOConnection connection)
    {
        connection.LockStateChanged -= availableSessionsUpdated;

        candidateSessions.Remove(connection.Identifier);
        AvailableSessionsUpdated?.Invoke();

        if (!activeSessions.TryGetValue(connection.Identifier, out var session))
            return;

        session.UnbindConnection();
    }

    private readonly SemaphoreSlim sessionManagementSemaphore = new SemaphoreSlim(1, 1);

    /// <summary>
    /// Attempt to connect to an existing session. If a session doesn't exist, an attempt to create it will be made, before connecting to it.
    /// </summary>
    /// <param name="sessionIdentifier">The identifier of the session to connect to</param>
    /// <param name="userName">The name of the user</param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException">The session isn't active <i>and</i> no EDMO robot is connected with the same identifier.</exception>
    /// <exception cref="UnauthorizedAccessException">The target robot is being used by another server.</exception>
    public EDMOSession.ControlContext AttemptConnectionTo(string sessionIdentifier, string userName)
    {
        sessionManagementSemaphore.Wait();

        if (activeSessions.TryGetValue(sessionIdentifier, out var session))
        {
            sessionManagementSemaphore.Release();
            var context = session.CreateContext(userName);
            AvailableSessionsUpdated?.Invoke();
            return context;
        }

        if (!candidateSessions.TryGetValue(sessionIdentifier, out var connection))
        {
            sessionManagementSemaphore.Release();
            throw new InvalidOperationException("Can't add user to a non-existent session");
        }

        if (connection.IsLocked)
            throw new UnauthorizedAccessException("The robot is being used by another server.");

        session = activeSessions[sessionIdentifier] =
            new EDMOSession(this, sessionIdentifier, SessionPluginLoader, connection);

        sessionManagementSemaphore.Release();
        var ctx = session.CreateContext(userName);
        AvailableSessionsUpdated?.Invoke();
        return ctx;
    }

    private void availableSessionsUpdated()
    {
        AvailableSessionsUpdated?.Invoke();
    }

    internal void UnregisterSession(string sessionIdentifier)
    {
        sessionManagementSemaphore.Wait();
        activeSessions.Remove(sessionIdentifier);
        sessionManagementSemaphore.Release();
    }
}
