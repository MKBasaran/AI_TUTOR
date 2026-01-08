using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ServerCore.Communication;
using ServerCore.EDMO.Communication.Commands;
using ServerCore.EDMO.Communication.Packets;

namespace ServerCore.EDMO.Communication;

/// <summary>
/// Describes a connection to specifically an EDMO device.
/// </summary>
/// <remarks>
/// Once constructed. A validation attempt is perform to validate whether the communication channel is connected to an EDMO robot.
/// </remarks>
public sealed class EDMOConnection
{
    private readonly ICommunicationChannel communicationChannel;

    // The status specific to this EDMOConnection
    private ConnectionStatus status { get; set; } = ConnectionStatus.Idle;

    /// <summary>
    /// <inheritdoc cref="ConnectionStatus"/>
    /// </summary>
    /// <remarks>
    ///  This is an aggregate status also takes into account the underlying communication channel
    /// </remarks>
    public ConnectionStatus Status =>
        (communicationChannel.Status is ConnectionStatus.Closed or ConnectionStatus.Failed)
            ? communicationChannel.Status
            : status;

    /// <summary>
    /// The identifier of this EDMOConnection.
    /// </summary>
    /// <remarks>
    /// In practice, this should always be non-null. If the value is <c>null</c>, then the connection is not establish, or the target is not a compliant EDMO robot.
    /// </remarks>
    public string? Identifier { get; private set; }

    /// <summary>
    /// The number of oscillators reported by the EDMO robot
    /// </summary>
    /// <remarks>
    /// If this is not given in the identification packet, then it is assumed to be 4.
    /// </remarks>
    public ushort OscillatorCount { get; private set; }

    /// <summary>
    /// The colours of each arm of the EDMO robot. The length of this array is expected to match <see cref="OscillatorCount"/>.
    /// </summary>
    /// <remarks>
    /// If this is not given in the identification packet. This will be defaulted to an array of zeros with length <see cref="OscillatorCount"/></remarks>
    public ushort[] ArmColourHues { get; private set; } = [];

    /// <summary>
    /// Whether the edmo robot is actively being controlled by another server.
    /// </summary>
    public bool IsLocked { get; private set; }

    /// <summary>
    /// An event raised when the robot lock state has changed.
    /// </summary>
    /// <seealso cref="IsLocked"/>
    public Action? LockStateChanged { get; set; }

    /// <inheritdoc cref="EDMOConnection"/>
    /// <param name="channel">The underlying communication channel</param>
    public EDMOConnection(ICommunicationChannel channel)
    {
        communicationChannel = channel;
        communicationChannel.DataReceived += handleData;

        _ = Task.Run(validateConnection);
    }

    private async Task validateConnection()
    {
        CancellationTokenSource cts = new();
        CancellationToken ct = cts.Token;

        byte[] data = [.."ED"u8, ..IdentificationCommand.BYTES, .."MO"u8];

        await communicationChannel.WriteAsync(data);
        status = ConnectionStatus.Waiting;
        // We only wait for 3s for validation;
        cts.CancelAfter(3000);

        while (!ct.IsCancellationRequested)
        {
            if (Identifier is not null)
            {
                status = ConnectionStatus.Connected;
                return;
            }

            // We defer cancellation responsibility to the loop
            await Task.Delay(100, CancellationToken.None);
        }

        status = ConnectionStatus.Failed;
        communicationChannel.Close();
    }

    private readonly List<byte> inputBuffer = [];
    private bool isReceivingPacket;

    private void handleData(ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
        {
            inputBuffer.Add(b);

            // Received header, unconditionally begin a new packet
            if (inputBuffer.Count >= 2)
            {
                if (inputBuffer[^1] == 'D' && inputBuffer[^2] == 'E')
                {
                    isReceivingPacket = true;
                    inputBuffer.Clear();
                }
                else if (!isReceivingPacket)
                {
                    inputBuffer.RemoveAt(0);
                }
            }

            if (!isReceivingPacket)
                continue;

            if (!(inputBuffer.Count >= 2 && inputBuffer[^1] == 'O' && inputBuffer[^2] == 'M'))
                continue;

            var packet = CollectionsMarshal.AsSpan(inputBuffer)[..^2];
            byte[] unescaped = EDMOPacket.UnescapePacket(packet);

            handlePacket(unescaped);
            isReceivingPacket = false;
            inputBuffer.Clear();
        }
    }

    /// <summary>
    ///  An event which is raised when this connection receives any <see cref="OscillatorDataPacket"/>.
    /// </summary>
    public OscillatorDataPacketHandler? OscillationDataReceived { get; set; }

    /// <summary>
    ///  An event which is raised when this connection receives any <see cref="IMUDataPacket"/>.
    /// </summary>
    public ImuDataPacketHandler? ImuDataReceived { get; set; }

    /// <summary>
    ///  An event which is raised when this connection receives any <see cref="TimePacket"/>.
    /// </summary>
    public TimePacketHandler? TimeReceived { get; set; }

    /// <summary>
    ///  An event which is raised when this connection receives any unknown packet.
    /// </summary>
    public UnknownPacketHandler? UnknownPacketReceived { get; set; }

    private void handlePacket(ReadOnlySpan<byte> data)
    {
        EDMOPacketType type = (EDMOPacketType)data[0];
        ReadOnlySpan<byte> payload = data[1..];

        try
        {
            switch (type)
            {
                case EDMOPacketType.Identify:
                    // The identifier string is a null-terminated string
                    int identifierLength = 0;
                    for (; identifierLength < payload.Length; ++identifierLength)
                    {
                        if (payload[identifierLength] == 0)
                            break;
                    }

                    string identifier = Encoding.ASCII.GetString(payload[..identifierLength]);

                    OscillatorCount = payload[identifierLength + 1];

                    int hueStartIndex = identifierLength + 2;

                    ArmColourHues = new ushort[OscillatorCount];

                    for (int i = 0; i < OscillatorCount; ++i)
                    {
                        ArmColourHues[i] = BitConverter.ToUInt16(payload[hueStartIndex..]);
                        hueStartIndex += sizeof(ushort);
                    }

                    bool newIsLocked = payload[hueStartIndex] == 1;

                    if (IsLocked != newIsLocked)
                    {
                        IsLocked = newIsLocked;
                        LockStateChanged?.Invoke();
                    }

                    // We set this last to ensure that all oscillator/hue properties are set *before* the connection is marked as good.
                    Identifier = identifier;

                    break;

                case EDMOPacketType.GetTime:
                {
                    var timeSyncPacket = EDMOPacket.Parse<TimePacket>(payload);
                    TimeReceived?.Invoke(this, timeSyncPacket);
                    break;
                }

                case EDMOPacketType.SendMotorData:
                {
                    var oscillatorState = EDMOPacket.Parse<OscillatorDataPacket>(payload);
                    OscillationDataReceived?.Invoke(this, oscillatorState);
                    break;
                }

                case EDMOPacketType.SendImuData:
                {
                    var imuData = EDMOPacket.Parse<IMUDataPacket>(payload);

                    ImuDataReceived?.Invoke(this, imuData);
                    break;
                }
                case EDMOPacketType.SendAllData:
                {
                    int offset = 0;
                    int timePacketSize = Marshal.SizeOf<TimePacket>();
                    var timeSyncPacket = EDMOPacket.Parse<TimePacket>(payload[offset..timePacketSize]);
                    TimeReceived?.Invoke(this, timeSyncPacket);
                    offset += timePacketSize;

                    int oscPacketSize = Marshal.SizeOf<OscillatorState>();

                    for (byte i = 0; i < OscillatorCount; ++i)
                    {
                        var oscillatorState =
                            EDMOPacket.Parse<OscillatorState>(payload[offset..(offset + oscPacketSize)]);
                        OscillationDataReceived?.Invoke(this, new OscillatorDataPacket(i, oscillatorState));
                        offset += oscPacketSize;
                    }

                    var imuData = EDMOPacket.Parse<IMUDataPacket>(payload[offset..(Marshal.SizeOf<IMUDataPacket>() +
                        offset)]);
                    ImuDataReceived?.Invoke(this, imuData);
                    break;
                }
                default:
                    UnknownPacketReceived?.Invoke(this, data);
                    break;
            }
        }
        catch (InvalidDataException)
        {
            UnknownPacketReceived?.Invoke(this, data);
        }
    }


    /// <summary>
    /// Send a packet to the connected EDMO robot asynchronously.
    /// </summary>
    /// <param name="packet">The packet to be sent.</param>
    /// <typeparam name="T">The unmanaged type that represents the layout of the packet</typeparam>
    public async Task WriteAsync<T>(T packet) where T : unmanaged
    {
        var span = MemoryMarshal.CreateReadOnlySpan(ref packet, 1);
        var packetBytes = MemoryMarshal.Cast<T, byte>(span);

        byte[] escapedBytes = [..EDMOPacket.HEADER, .. EDMOPacket.EscapePacket(packetBytes), .. EDMOPacket.FOOTER];

        await communicationChannel.WriteAsync(escapedBytes);
    }

    /// <summary>
    /// Send a packet to the connected EDMO robot.
    /// </summary>
    /// <param name="packet">The packet to be sent.</param>
    /// <typeparam name="T">The unmanaged type that represents the layout of the packet</typeparam>
    public void Write<T>(T packet) where T : unmanaged
    {
        var span = MemoryMarshal.CreateReadOnlySpan(ref packet, 1);
        var packetBytes = MemoryMarshal.Cast<T, byte>(span);

        byte[] escapedBytes = [..EDMOPacket.HEADER, .. EDMOPacket.EscapePacket(packetBytes), .. EDMOPacket.FOOTER];

        communicationChannel.Write(escapedBytes);
    }

    /// <summary>
    /// Describes a handler for oscillator data packets received from this EDMO connection.
    /// </summary>
    public delegate void OscillatorDataPacketHandler(EDMOConnection connection, in OscillatorDataPacket packet);

    /// <summary>
    /// Describes a handler for IMU data packets received from this EDMO connection.
    /// </summary>
    public delegate void ImuDataPacketHandler(EDMOConnection connection, in IMUDataPacket packet);

    /// <summary>
    /// Describes a handler for time synchronisation packets received from this EDMO connection.
    /// </summary>
    public delegate void TimePacketHandler(EDMOConnection connection, in TimePacket timestamp);

    /// <summary>
    /// Describes a handler for unknown packets received from this EDMO connection.
    /// </summary>
    public delegate void UnknownPacketHandler(EDMOConnection connection, ReadOnlySpan<byte> dataBytes);
}
