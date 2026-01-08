using System.Runtime.InteropServices;

namespace ServerCore.EDMO.Communication.Commands;

/// <summary>
/// Represents a layout accurate representation of the command to signal the end of an EDMO session.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct SessionEndCommand()
{
   private readonly EDMOPacketType command = EDMOPacketType.SessionEnd;
};
