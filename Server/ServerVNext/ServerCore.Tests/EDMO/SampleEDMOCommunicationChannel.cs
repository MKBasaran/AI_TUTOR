using System.Diagnostics;
using ServerCore.EDMO.Communication.Packets;
using ServerCore.Communication;
using ServerCore.EDMO.Communication;
using System.Numerics;
using System.Runtime.InteropServices;

namespace ServerCore.Tests.EDMO;

public class SampleEDMOCommunicationChannel : ICommunicationChannel
{
    public IMUData ReferenceIMUData { get; }

    private OscillatorState[] oscillatorStates = new OscillatorState[4];

    public ReadOnlySpan<OscillatorState> ReferenceOscillatorStates => oscillatorStates;

    private CancellationTokenSource cts = new();

    private Task updateTask;

    private bool sessionStarted = false;

    private DateTime sessionStartTime = DateTime.Now;
    private uint sessionStartOffset = 0;

    private async Task sendLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (sessionStarted)
            {
                sendAllData();
            }

            await Task.Delay(50, CancellationToken.None);
        }
    }

    public SampleEDMOCommunicationChannel()
    {
        ReferenceIMUData = new IMUData
        {
            Gyroscope = new SensorInfo<Vector3> { Timestamp = 56, Accuracy = 5, Data = new Vector3(5, 6, 3) },
            Accelerometer = new SensorInfo<Vector3> { Timestamp = 25, Accuracy = 3, Data = new Vector3(0, 78, 3) },
            MagneticField = new SensorInfo<Vector3> { Timestamp = 25, Accuracy = 3, Data = new Vector3(0, 78, 3), },
            Gravity = new SensorInfo<Vector3> { Timestamp = 234, Accuracy = 33, Data = new Vector3(0, 68, 3), },
            Rotation = new SensorInfo<Quaternion>
            {
                Timestamp = 2, Accuracy = 33, Data = new Quaternion(0, 682, 3, 2),
            }
        };

        var rng = Random.Shared;
        for (int i = 0; i < 4; ++i)
        {
            oscillatorStates[i] = new OscillatorState
            {
                Frequency = rng.NextSingle(),
                Amplitude = rng.NextSingle(),
                Offset = rng.NextSingle(),
                PhaseShift = rng.NextSingle(),
                Phase = rng.NextSingle()
            };
        }
    }

    public void Dispose() { }

    public ConnectionStatus Status => ConnectionStatus.Connected;

    private List<byte> buffer = [];

    public Task WriteAsync(ReadOnlyMemory<byte> data)
    {
        Write(data.Span);
        return Task.CompletedTask;
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        buffer.AddRange(data);

        ReadOnlySpan<byte> span = buffer.ToArray();

        if (!(span.StartsWith("ED"u8) && span.EndsWith("MO"u8)))
            return;

        var commandReceived = (EDMOPacketType)span[2];

        switch (commandReceived)
        {
            case EDMOPacketType.Identify:
                WriteAsCommunicatee("ED\0"u8 + @"E\DM\OckMO"u8);
                break;

            case EDMOPacketType.GetTime:
                Span<byte> packet = [(byte)EDMOPacketType.GetTime, 0xff, 0x00, 0x00, 0x00];
                ReadOnlySpan<byte> escapedTimeBits = EDMOPacket.EscapePacket(packet);
                WriteAsCommunicatee([.."ED"u8, ..escapedTimeBits, .."MO"u8]);
                break;

            case EDMOPacketType.SendMotorData:
                for (byte i = 0; i < 4; ++i)
                {
                    ref OscillatorState state = ref oscillatorStates[i];

                    byte[] incomingPacket =
                    [
                        (byte)EDMOPacketType.SendMotorData, i, .. asBytesF(state.Frequency),
                        ..asBytesF(state.Amplitude), ..asBytesF(state.Offset),
                        ..asBytesF(state.PhaseShift), ..asBytesF(state.Phase)
                    ];

                    byte[] escapedOscPacket = EDMOPacket.EscapePacket(incomingPacket);

                    WriteAsCommunicatee([.."ED"u8, ..escapedOscPacket, .."MO"u8]);
                }

                break;
            case EDMOPacketType.SendImuData:
                byte[] imupacket =
                [
                    (byte)EDMOPacketType.SendImuData,
                    ..asBytesU32(ReferenceIMUData.Gyroscope.Timestamp),
                    ReferenceIMUData.Gyroscope.Accuracy, 0, 0, 0,
                    ..asBytesF(ReferenceIMUData.Gyroscope.Data.X),
                    ..asBytesF(ReferenceIMUData.Gyroscope.Data.Y),
                    ..asBytesF(ReferenceIMUData.Gyroscope.Data.Z),

                    ..asBytesU32(ReferenceIMUData.Accelerometer.Timestamp),
                    ReferenceIMUData.Accelerometer.Accuracy, 0, 0, 0,
                    ..asBytesF(ReferenceIMUData.Accelerometer.Data.X),
                    ..asBytesF(ReferenceIMUData.Accelerometer.Data.Y),
                    ..asBytesF(ReferenceIMUData.Accelerometer.Data.Z),

                    ..asBytesU32(ReferenceIMUData.MagneticField.Timestamp),
                    ReferenceIMUData.MagneticField.Accuracy, 0, 0, 0,
                    ..asBytesF(ReferenceIMUData.MagneticField.Data.X),
                    ..asBytesF(ReferenceIMUData.MagneticField.Data.Y),
                    ..asBytesF(ReferenceIMUData.MagneticField.Data.Z),

                    ..asBytesU32(ReferenceIMUData.Gravity.Timestamp),
                    ReferenceIMUData.Gravity.Accuracy, 0, 0, 0,
                    ..asBytesF(ReferenceIMUData.Gravity.Data.X),
                    ..asBytesF(ReferenceIMUData.Gravity.Data.Y),
                    ..asBytesF(ReferenceIMUData.Gravity.Data.Z),

                    ..asBytesU32(ReferenceIMUData.Rotation.Timestamp),
                    ReferenceIMUData.Rotation.Accuracy, 0, 0, 0,
                    ..asBytesF(ReferenceIMUData.Rotation.Data.X),
                    ..asBytesF(ReferenceIMUData.Rotation.Data.Y),
                    ..asBytesF(ReferenceIMUData.Rotation.Data.Z),
                    ..asBytesF(ReferenceIMUData.Rotation.Data.W)
                ];

                byte[] escaped = EDMOPacket.EscapePacket(imupacket);

                WriteAsCommunicatee([.."ED"u8, .. escaped, .."MO"u8]);
                break;
            case EDMOPacketType.SessionStart:
                if (!sessionStarted)
                {
                    updateTask = Task.Run(() => sendLoop(cts.Token));
                    sessionStarted = true;
                }

                var payload = data[3..];
                uint newOffset = BitConverter.ToUInt32(payload);
                sessionStartOffset = newOffset;
                sessionStartTime = DateTime.Now;

                break;
        }

        buffer.Clear();
        return;

        ReadOnlySpan<byte> asBytesF(float f) => BitConverter.GetBytes(f);
        ReadOnlySpan<byte> asBytesU32(uint i) => BitConverter.GetBytes(i);
    }

    private void sendAllData()
    {
        uint millis = (uint)(DateTime.Now - sessionStartTime).TotalMilliseconds + sessionStartOffset;
        List<byte> payload = [(byte)EDMOPacketType.SendAllData, ..BitConverter.GetBytes(millis)];

        WriteAsCommunicatee("ED"u8);
        foreach (var oscillatorState in oscillatorStates)
        {
            payload.AddRange(getBytes(oscillatorState));
        }
        payload.AddRange(getBytes(ReferenceIMUData));

        var escaped = EDMOPacket.EscapePacket(payload.ToArray());

        WriteAsCommunicatee(escaped);
        WriteAsCommunicatee("MO"u8);
    }


    private byte[] getBytes<T>(T data) where T : unmanaged
    {
        int size = Marshal.SizeOf(data);
        byte[] bytes = new byte[size];
        IntPtr ptr = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(data, ptr, false);
            Marshal.Copy(ptr, bytes, 0, size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        return bytes;
    }

    public void WriteAsCommunicatee(ReadOnlySpan<byte> data)
    {
        DataReceived?.Invoke(data);
    }


    public ICommunicationChannel.DataReceivedAction? DataReceived { get; set; }

    public void Close()
    {
        cts.Cancel();
    }
}
