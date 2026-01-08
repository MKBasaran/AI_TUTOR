using System.Runtime.InteropServices;

namespace ServerCore.EDMO.Communication.Packets;

/// <summary>
/// The struct that contains the current state of a single oscillator.
/// </summary>
/// <param name="Frequency">The current frequency.</param>
/// <param name="Amplitude">The current amplitude.</param>
/// <param name="Offset">The current offset.</param>
/// <param name="PhaseShift">The current phase shift.</param>
/// <param name="Phase">The current phase.</param>
[StructLayout(LayoutKind.Sequential)]
public record struct OscillatorState(
    float Frequency,
    float Amplitude,
    float Offset,
    float PhaseShift,
    float Phase);


/// <summary>
/// The struct describing the layout of the Oscillator Data packet sent by the EDMO firmware.
/// </summary>
/// <param name="OscillatorIndex">The index of the oscillator</param>
/// <param name="OscillatorState">The current state of the oscillator</param>
[StructLayout(LayoutKind.Explicit, Pack = 1)]
public readonly record struct OscillatorDataPacket(byte OscillatorIndex, OscillatorState OscillatorState)
{
    /// <summary>
    /// The index of the oscillator.
    /// </summary>
    [FieldOffset(0)] public readonly byte OscillatorIndex = OscillatorIndex;

    /// <summary>
    /// The current state of the oscillator.
    /// </summary>
    [FieldOffset(1)] public readonly OscillatorState OscillatorState = OscillatorState;
}
