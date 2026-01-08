using System;
using System.Runtime.InteropServices;

namespace ServerCore.EDMO.Communication.Packets;

/// <summary>
/// A struct describing the layout of the Identification packet sent by the EDMO firmware.
/// </summary>
/// <remarks>
/// This is not directly used in the code. Marshalling is done manually in <see cref="EDMOConnection"/>>. This struct is only kept to describe the layout of the identification packet.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct IdentificationPacket
{
    /// <summary>
    /// The identifier of the EDMO robot
    /// </summary>
    public required string Identifier { get; init; }

    /// <summary>
    /// The number of oscillators/arm associated with the robot
    /// </summary>
    public int OscillatorCount { get; init; }

    /// <summary>
    /// The colours used by each oscillator/arm
    /// </summary>
    public short[] Hues { get; init; }
};
