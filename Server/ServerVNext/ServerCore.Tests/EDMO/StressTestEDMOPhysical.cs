using System.Text;
using ServerCore.EDMO.Communication;
using ServerCore.EDMO.Communication.Commands;
using ServerCore.EDMO.Communication.Packets;
using ServerCore.Logging;

namespace ServerCore.Tests.EDMO;

[TestClass]
public class StressTestEDMOPhysical
{
    public TestContext ctx { get; set; }

    private FusedEDMOConnection connection;
    private ILogger testLogger = new ConsoleLogger("StressTestLogs");

    private void waitUntilConnectionEstablished()
    {
        CancellationTokenSource source = new();
        var token = source.Token;

        source.CancelAfter(10000);

        while (connection is null)
        {
            token.ThrowIfCancellationRequested();
        }
    }

    [TestMethod]
    public void StressTestActualEDMO()
    {
        EDMOConnectionManager connectionManager = new();
        connectionManager.EDMOConnectionEstablished += onEDMOConnected;
        connectionManager.Start();

        try
        {
            waitUntilConnectionEstablished();
        }
        catch (OperationCanceledException)
        {
            throw new SystemException("No EDMO connected");
        }

        Console.WriteLine("EDMO Connected");

        DateTime startTime = DateTime.Now;
        while ((DateTime.Now - startTime).TotalSeconds < 120)
        {
        }
    }

    private void onEDMOConnected(FusedEDMOConnection edmoConnection)
    {
        connection = edmoConnection;

        connection.TimeReceived += handle;
        connection.OscillationDataReceived += handle;
        connection.ImuDataReceived += handle;
        connection.UnknownPacketReceived += (_, data) =>
        {
            StringBuilder builder = new();

            foreach (byte b in data)
                builder.Append(b);

            testLogger.Log($"Unknown packet: {builder}");
            throw new InvalidDataException("Unknown packet found.");
        };

        for (byte i = 0; i < 4; ++i)
        {
            connection.Write(new UpdateOscillatorCommand(i, new OscillatorParams
            {
                Amplitude = 90,
                Frequency = 0.5f,
                Offset = 90f,
                PhaseShift = 0
            }));
        }

        connection.Write(new UpdateOscillatorCommand());
        connection.Write(new SessionStartCommand());

        void handle<T>(EDMOConnection _, in T data) where T : unmanaged
        {
            testLogger.Log(data);
        }
    }
}
