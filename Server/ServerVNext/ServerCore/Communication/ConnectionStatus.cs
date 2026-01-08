namespace ServerCore.Communication;

/// <summary>
/// Provides information about the state of the underlying connection.
/// </summary>
public enum ConnectionStatus
{
    /// <summary>
    /// The connection is not doing anything. Possibly just created.
    /// </summary>
    Idle,

    /// <summary>
    /// The connection has been properly initialised, and is waiting for establishment
    /// </summary>
    Waiting,

    /// <summary>
    /// A connection was successfully made.
    /// </summary>
    Connected,

    /// <summary>
    /// The connection has failed.
    /// </summary>
    Failed,

    /// <summary>
    /// The connection has been closed due to voluntary or involuntary disconnection after a successful connection has been made.
    /// </summary>
    Closed
}
