using System;
using System.Runtime.InteropServices;

namespace ServerCore.EDMO.Communication.Commands;
/// <summary>
/// A struct describing the layout of the identication command sent by the server.
///
/// It includes an identifier for the server instance, which is used by the edmo robot to lock itself, preventing other servers from connecting.
/// </summary>
/// <remarks>
/// Since all instances of this struct will have the same contents, if marshalling is desired, opt to use <see cref="IdentificationCommand.BYTES"/> to reduce allocations.
/// </remarks>
[StructLayout(LayoutKind.Explicit)]
public readonly record struct IdentificationCommand()
{
    private static readonly Guid instance_guid = Guid.NewGuid();
    /// <summary>
    /// The layout of this
    /// </summary>
    public static readonly byte[] BYTES = [..MemoryMarshal.AsBytes([new IdentificationCommand()])];

    [FieldOffset(0)] private readonly byte command = (byte)EDMOPacketType.Identify;
    [FieldOffset(1)] private readonly Guid guid = instance_guid;
}
