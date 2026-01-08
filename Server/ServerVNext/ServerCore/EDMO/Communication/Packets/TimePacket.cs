using System.Runtime.InteropServices;

namespace ServerCore.EDMO.Communication.Packets;

/// <summary>
/// A struct describing the layout of the Time Packet sent by the EDMO firmware.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public record struct TimePacket(uint Time);
