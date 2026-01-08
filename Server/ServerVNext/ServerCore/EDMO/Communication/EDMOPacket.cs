using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace ServerCore.EDMO.Communication;

/// <summary>
/// A class containing helper methods for packet related operations.
/// </summary>
public static class EDMOPacket
{
    /// <summary>
    /// The standard packet header of EDMO communication
    /// </summary>
    public static readonly byte[] HEADER = [.."ED"u8];

    /// <summary>
    /// The standard packet footer of an EDMO communication
    /// </summary>
    public static readonly byte[] FOOTER = [.."MO"u8];

    /// <summary>
    /// Unescape an array of bytes, returning it into a parseable form.
    /// </summary>
    /// <param name="bytes">The escaped payload</param>
    /// <returns>The original payload</returns>
    /// <seealso cref="EscapePacket"/>
    /// <remarks>
    /// <para>Unescaping an unescaped payload doesn't imply an identity operation. Attempts to do so may mangle the payload in an irrecoverable way.</para>
    /// </remarks>
    public static byte[] UnescapePacket(ReadOnlySpan<byte> bytes)
    {
        List<byte> unescapedBytes = [];

        for (int i = 0; i < bytes.Length; ++i)
        {
            byte b = bytes[i];

            if (b == '\\' && i < bytes.Length - 1)
            {
                unescapedBytes.Add(bytes[i + 1]);
                i += 1;
                continue;
            }

            unescapedBytes.Add(b);
        }

        return [..unescapedBytes];
    }

    /// <summary>
    /// Escape an array of bytes. Escaping any occurrence of <see cref="HEADER"/> and <see cref="FOOTER"/>, so that the payload doesn't incorrectly signal the start of end of a packet.
    /// </summary>
    /// <param name="bytes">The unescaped payload.</param>
    /// <returns>
    /// The payload, escaped.
    /// </returns>
    /// <seealso cref="UnescapePacket"/>
    /// <remarks>
    /// Escaping an escaped payload implies an identity operation, and the return value would be equivalent to the input value.
    /// This is because after the first escape, the payload is guaranteed not to contain <see cref="HEADER"/> or <see cref="FOOTER"/>, and the operation does nothing.
    /// </remarks>
    public static byte[] EscapePacket(ReadOnlySpan<byte> bytes)
    {
        List<byte> escapedBytes = [];

        for (int i = 0; i < bytes.Length; ++i)
        {
            // Because the backlash acts as an escape character, we also need to escape that if we encounter it
            if (bytes[i] == '\\')
            {
                escapedBytes.Add((byte)'\\');
                escapedBytes.Add((byte)'\\');
                continue;
            }

            if (i > 0)
            {
                bool matchesHeader = bytes[i] == HEADER[1] && bytes[i - 1] == HEADER[0];
                bool matchesFooter = bytes[i] == FOOTER[1] && bytes[i - 1] == FOOTER[0];

                if (matchesFooter || matchesHeader)
                    escapedBytes.Add((byte)'\\');
            }

            escapedBytes.Add(bytes[i]);
        }

        return [..escapedBytes];
    }

    /// <summary>
    /// Takes an unescaped payload, and attempts to cast it into a struct. Taking into account struct layouts.
    /// </summary>
    /// <param name="bytes">The original payload, unescaped</param>
    /// <typeparam name="T">An unmanaged struct that describes the layout of the payload</typeparam>
    /// <returns>A struct that contains the payload of the data</returns>
    /// <exception cref="InvalidDataException">The payload length doesn't match the size of <c>T</c></exception>
    public static T Parse<T>(ReadOnlySpan<byte> bytes) where T : unmanaged
    {
        if (bytes.Length != Marshal.SizeOf<T>())
            throw new InvalidDataException(
                $"Number of bytes ({bytes.Length}) does not match the size of struct <{nameof(T)}> ({Marshal.SizeOf<T>()}).");

        return MemoryMarshal.Cast<byte, T>(bytes)[0];
    }
}
