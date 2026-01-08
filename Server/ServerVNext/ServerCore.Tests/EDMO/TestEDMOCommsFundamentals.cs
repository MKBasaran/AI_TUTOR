using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using ServerCore.EDMO.Communication;
using ServerCore.EDMO.Communication.Commands;
using ServerCore.EDMO.Communication.Packets;


namespace ServerCore.Tests.EDMO;

[TestClass]
public class TestEDMOCommsFundamentals
{
    private static readonly byte[][] unescaped_data = new byte[][]
    {
        [.."The quick brown fox jumps over the lazy dog."u8],
        [
            ..@"The EDMO is a low cost robot that is aimed to introduce concepts of robotics to children and new students."u8
        ],
        [..@"ED, EDD, and EDDY"u8],
        [..@"\bin\user\"u8],
    };

    private static readonly byte[][] escaped_data = new byte[][]
    {
        [.."The quick brown fox jumps over the lazy dog."u8],
        [
            ..@"The E\DM\O is a low cost robot that is aimed to introduce concepts of robotics to children and new students."u8
        ],
        [..@"E\D, E\DD, and E\DDY"u8],
        [..@"\\bin\\user\\"u8],
    };

    private EDMOConnection edmoConnection = null!;
    private SampleEDMOCommunicationChannel communicationChannel = null!;

    private List<OscillatorDataPacket> oscillatorDataPackets = null!;
    private List<IMUDataPacket> imuDataPackets = null!;
    private List<TimePacket> timePackets = null!;

    [TestInitialize]
    public void Init()
    {
        communicationChannel = new SampleEDMOCommunicationChannel();
        edmoConnection = new EDMOConnection(communicationChannel);

        oscillatorDataPackets = [];
        imuDataPackets = [];
        timePackets = [];

        edmoConnection.ImuDataReceived += addImuPacket;
        edmoConnection.OscillationDataReceived += addOscillatorData;
        edmoConnection.TimeReceived += addTimePackets;

        while (true)
        {
            if (edmoConnection.Identifier == "EDMOck")
                break;
        }

        return;

        void addImuPacket(EDMOConnection _, in IMUDataPacket packet) => imuDataPackets.Add(packet);
        void addOscillatorData(EDMOConnection _, in OscillatorDataPacket packet) => oscillatorDataPackets.Add(packet);
        void addTimePackets(EDMOConnection _, in TimePacket packet) => timePackets.Add(packet);
    }


    [TestMethod]
    public void TestPacketUnescape()
    {
        Debug.Assert(unescaped_data.Length == escaped_data.Length);

        for (int i = 0; i < unescaped_data.Length; ++i)
        {
            byte[] input = escaped_data[i];
            byte[] expected = unescaped_data[i];

            byte[] unescaped = EDMOPacket.UnescapePacket(input);

            try
            {
                Assert.IsTrue(unescaped.SequenceEqual(expected));
            }
            catch
            {
                Console.WriteLine($"Input: {Encoding.ASCII.GetString(input)}");
                Console.WriteLine($"Output: {Encoding.ASCII.GetString(unescaped)}");
                Console.WriteLine($"Expected: {Encoding.ASCII.GetString(expected)}");

                throw;
            }
        }
    }

    [TestMethod]
    public void TestPacketEscape()
    {
        Debug.Assert(unescaped_data.Length == escaped_data.Length);

        for (int i = 0; i < unescaped_data.Length; ++i)
        {
            byte[] input = unescaped_data[i];
            byte[] expected = escaped_data[i];

            byte[] escaped = EDMOPacket.EscapePacket(input);

            try
            {
                Assert.IsTrue(expected.SequenceEqual(escaped));
            }
            catch
            {
                Console.WriteLine($"Input: {Encoding.ASCII.GetString(input)}");
                Console.WriteLine($"Output: {Encoding.ASCII.GetString(escaped)}");
                Console.WriteLine($"Expected: {Encoding.ASCII.GetString(expected)}");

                throw;
            }
        }
    }

    // This ensures that Unescape(Escape(x)) == x
    [TestMethod]
    public void TestEscapeThenUnescape()
    {
        var rng = Random.Shared;
        for (int i = 0; i < 1024; ++i)
        {
            int length = rng.Next(0, 1024);

            byte[] bytes = new byte[length];
            rng.NextBytes(bytes);

            for (int j = 0; j < bytes.Length; ++j)
            {
                // Stray escape characters aren't great, and would be removed unconditionally
                if (bytes[j] == '\\')
                    bytes[j] = 0;
            }

            byte[] escaped = EDMOPacket.EscapePacket(bytes);
            byte[] unescaped = EDMOPacket.UnescapePacket(escaped);

            try
            {
                Assert.IsTrue(unescaped.SequenceEqual(bytes));
            }
            catch
            {
                Console.WriteLine($"Input: {Encoding.ASCII.GetString(bytes)}");
                Console.WriteLine($"Escaped: {Encoding.ASCII.GetString(escaped)}");
                Console.WriteLine($"Unescaped: {Encoding.ASCII.GetString(unescaped)}");
                throw;
            }
        }
    }

    [TestMethod]
    public void TestParseWTFPacket()
    {
        ReadOnlySpan<byte> testFrontierPacket = [243, .. "The quick brown fox jumps over your doofy mage."u8];
        communicationChannel.WriteAsCommunicatee(testFrontierPacket);

        Assert.IsTrue(oscillatorDataPackets.Count == 0);
        Assert.IsTrue(timePackets.Count == 0);
        Assert.IsTrue(imuDataPackets.Count == 0);
    }

    [TestMethod]
    public void TestReceiveIdentityPacket()
    {
        byte[] testFrontierPacket = [.."ED"u8, (byte)EDMOPacketType.Identify, .. "Frontier"u8, .."MO"u8];

        communicationChannel.WriteAsCommunicatee(testFrontierPacket);

        Assert.IsTrue(oscillatorDataPackets.Count == 0);
        Assert.IsTrue(timePackets.Count == 0);
        Assert.IsTrue(imuDataPackets.Count == 0);

        Assert.IsTrue(edmoConnection.Identifier == "Frontier");
    }

    [TestMethod]
    public void TestReceivedTimePacket()
    {
        edmoConnection.Write((byte)EDMOPacketType.GetTime);

        Assert.IsTrue(oscillatorDataPackets.Count == 0);
        Assert.IsTrue(timePackets.Count == 1);
        Assert.IsTrue(imuDataPackets.Count == 0);

        Assert.IsTrue(timePackets.First().Time == 255);
    }

    [TestMethod]
    public void TestReceivedOversizedTimePacket()
    {
        // Both the M0 and most PC's are little endian
        byte[] incomingPacket = [.."ED"u8, (byte)EDMOPacketType.GetTime, 0xff, 0x00, 0x00, 0x00, 0x00, .."MO"u8];

        communicationChannel.WriteAsCommunicatee(incomingPacket);

        Assert.IsTrue(oscillatorDataPackets.Count == 0);
        Assert.IsTrue(timePackets.Count == 0);
        Assert.IsTrue(imuDataPackets.Count == 0);
    }


    [TestMethod]
    public void TestReceiveOscillatorPacket()
    {
        edmoConnection.Write((byte)EDMOPacketType.SendMotorData);

        Assert.IsTrue(oscillatorDataPackets.Count == communicationChannel.ReferenceOscillatorStates.Length);
        Assert.IsTrue(timePackets.Count == 0);
        Assert.IsTrue(imuDataPackets.Count == 0);

        Assert.IsTrue(oscillatorDataPackets.All(p =>
            communicationChannel.ReferenceOscillatorStates.Contains(p.OscillatorState)));
    }

    [TestMethod]
    public void TestMalformedOscillatorPacket()
    {
        var rng = Random.Shared;
        float freq = rng.NextSingle();
        float amp = rng.NextSingle();
        float offset = rng.NextSingle();
        float phaseshift = rng.NextSingle();
        float phase = rng.NextSingle();

        ReadOnlySpan<byte> asBytes(float f) => BitConverter.GetBytes(f);

        byte[] incomingPacket =
        [
            (byte)EDMOPacketType.SendMotorData, 25, .. asBytes(freq), ..asBytes(amp), ..asBytes(offset),
            ..asBytes(phaseshift), ..asBytes(phase), 0
        ];

        communicationChannel.WriteAsCommunicatee(incomingPacket);

        Assert.IsTrue(oscillatorDataPackets.Count == 0);
        Assert.IsTrue(timePackets.Count == 0);
        Assert.IsTrue(imuDataPackets.Count == 0);
    }

    [TestMethod]
    public void TestIMUPacket()
    {
        edmoConnection.Write((byte)EDMOPacketType.SendImuData);

        Assert.IsTrue(oscillatorDataPackets.Count == 0);
        Assert.IsTrue(timePackets.Count == 0);
        Assert.IsTrue(imuDataPackets.Count == 1);

        Assert.IsTrue(imuDataPackets.First().Data == communicationChannel.ReferenceIMUData);
    }

    private void waitUntil(Func<bool> predicate, int timeout = 100000)
    {
        CancellationTokenSource source = new();
        var token = source.Token;

        source.CancelAfter(timeout);

        while (!predicate())
        {
            token.ThrowIfCancellationRequested();
        }
    }

    [TestMethod]
    public void TestSessionAllDataPackets()
    {
        edmoConnection.Write(new SessionStartCommand(0));

        waitUntil(() => timePackets.Count >= 100, 60000);
    }

    [TestMethod]
    public void TestSessionStartTime()
    {
        uint startTime = 0;
        edmoConnection.Write(new SessionStartCommand(0));

        waitUntil(() => timePackets.Count > 0);

        uint magicOffset = (uint)Random.Shared.Next();

        var timePacket = timePackets.Last();

        Assert.IsTrue(timePacket.Time >= 0 && timePacket.Time < magicOffset);

        edmoConnection.Write(new SessionStartCommand(magicOffset));
        timePackets.Clear();

        waitUntil(() => timePackets.Count > 0);
        timePacket = timePackets.Last();

        Assert.IsTrue(timePacket.Time >= magicOffset);
    }
}
