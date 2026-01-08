using System.Runtime.InteropServices;

namespace ServerCore.EDMO.Communication.Commands;

/// <summary>
/// Represents a layout accurate representation of the command to signal the start of an EDMO session.
/// </summary>
/// <param name="ReferenceTime">The timestamp to synchronise to.</param>
[StructLayout(LayoutKind.Explicit, Pack = 1)]
public readonly record struct SessionStartCommand(uint ReferenceTime)
{
    [FieldOffset(0)] private readonly EDMOPacketType command = EDMOPacketType.SessionStart;

    /// <summary>
    /// The timestamp to synchronise to
    /// </summary>
    [FieldOffset(1)] public readonly uint ReferenceTime = ReferenceTime;
};
