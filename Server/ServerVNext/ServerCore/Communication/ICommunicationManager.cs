using System;

namespace ServerCore.Communication;

/// <summary>
/// A abstraction for searching and keeping track of <see cref="ICommunicationChannel"/>s.
/// <br/>
/// Searching and management happens in the background, parallel to existing code.
/// </summary>
/// <remarks>
/// In order to ensure all channels are received properly, it is essential that users of this class assigns <see cref="CommunicationChannelEstablished"/> <i>before</i> calling <see cref="Start"/>.
/// </remarks>
public interface ICommunicationManager : IDisposable
{
    /// <summary>
    /// An event which is raised when a communication channel is established
    /// </summary>
    CommunicationChannelEstablishedAction? CommunicationChannelEstablished { get; set; }

    /// <summary>
    /// An event which is raised when a communication channel is lost.
    /// </summary>
    CommunicationChannelLostAction? CommunicationChannelLost { get; set; }

    /// <summary>
    /// Starts this communication manager. This communication manager will search for and attempt connections asynchronously.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops this communication manager. This communication manager will no longer search for communication channels.
    /// </summary>
    void Stop();

    /// <summary>
    /// Represents a handler for receiving an <see cref="ICommunicationChannel"/> from an <see cref="ICommunicationManager"/>
    /// </summary>
    /// <param name="channel">The newly connected channel</param>
    delegate void CommunicationChannelEstablishedAction(ICommunicationChannel channel);

    /// <summary>
    /// Represents a handler that respond to an <see cref="ICommunicationChannel"/> being disconnected/lost.
    /// </summary>
    /// <param name="channel">The lost channel</param>
    delegate void CommunicationChannelLostAction(ICommunicationChannel channel);
}
