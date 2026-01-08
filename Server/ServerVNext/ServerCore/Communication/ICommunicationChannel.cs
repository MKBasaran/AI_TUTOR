using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace ServerCore.Communication;

/// <summary>
/// Represents a connection to an external entity
/// </summary>
public interface ICommunicationChannel : IDisposable
{
    /// <summary>
    /// <inheritdoc cref="ConnectionStatus"/>
    /// </summary>
    ConnectionStatus Status { get; }

    /// <summary>
    /// Send arbitrary data through the communication channel asynchronously.
    /// </summary>
    /// <param name="data">The data to be sent</param>
    Task WriteAsync(ReadOnlyMemory<byte> data);

    /// <summary>
    /// Send arbitrary data through the communication channel.
    /// </summary>
    /// <param name="data">The data to be sent</param>
    void Write(ReadOnlySpan<byte> data);

    /// <summary>
    ///  An event which is raised when <see cref="ICommunicationChannel"/> receives any data.
    /// </summary>
    DataReceivedAction? DataReceived { get; set; }

    /// <summary>
    /// Close the communication channel, and the underlying implementation.
    /// </summary>
    void Close();

    /// <summary>
    /// Represents a handler for data received from an <see cref="ICommunicationChannel"/>
    /// </summary>
    /// <param name="data">The data received.</param>
    delegate void DataReceivedAction(ReadOnlySpan<byte> data);
}
