using System.Runtime.InteropServices;

namespace ServerCore.EDMO.Communication.Commands;

/// <summary>
/// A struct describing the layout of oscillator parameters as expected by EDMO firmware.
/// </summary>
/// <param name="Frequency">The target frequency of the oscillator</param>
/// <param name="Amplitude">The target range/amplitude of the oscillator</param>
/// <param name="Offset">The target offset of the oscillator. Also known as the baseline position.</param>
/// <param name="PhaseShift">The target phase shift of the oscillator</param>
[StructLayout(LayoutKind.Sequential)]
public record struct OscillatorParams(float Frequency, float Amplitude, float Offset, float PhaseShift);

/// <summary>
/// A struct describing the layout of the command to update individual oscillators as expected by EDMO firmware
/// </summary>
/// <param name="OscillatorIndex">The index of the oscillator</param>
/// <param name="OscillatorParams">The parameters of the oscillator</param>
[StructLayout(LayoutKind.Explicit, Pack = 1)]
public readonly record struct UpdateOscillatorCommand(byte OscillatorIndex, OscillatorParams OscillatorParams)
{
    [FieldOffset(0)] private readonly byte Command = (byte)EDMOPacketType.UpdateOscillator;

    /// <summary>
    /// The target oscillator
    /// </summary>
    [FieldOffset(1)] public readonly byte OscillatorIndex = OscillatorIndex;

    /// <summary>
    /// <inheritdoc cref="OscillatorParams"/>
    /// </summary>
    [FieldOffset(2)] public readonly OscillatorParams OscillatorParams = OscillatorParams;
}
