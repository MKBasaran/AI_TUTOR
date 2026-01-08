using System.Net.Sockets;
using System.Text;
using ServerCore.Communication;
using ServerCore.Communication.UDP;

namespace ServerCore.Tests.Communication.UDP;

[TestClass]
public sealed class TestUdpCommunicationManager
{
    public TestContext TestContext { get; set; }

    private UdpCommunicationManager udpCommunicationManager = null!;

    [TestInitialize()]
    public void Setup()
    {
        const int TEST_PORT = 12000;
        udpCommunicationManager = new UdpCommunicationManager(TEST_PORT)
        {
            PollMessage = "The quick brown fox jumps over the lazy dog.",
        };
    }

    [TestCleanup()]
    public void Teardown()
    {
        udpCommunicationManager.Stop();
    }

    private bool waitUntilTrue(Func<bool> condition, double timeout)
    {
        var startTime = DateTime.Now;
        while ((DateTime.Now - startTime).TotalSeconds < timeout)
        {
            if (condition())
                return true;
        }

        return false;
    }


    [TestMethod]
    // ReSharper disable once InconsistentNaming
    public void TestUDPCommunicationManager()
    {
        List<ICommunicationChannel> communicationChannels = [];
        List<string> receivedCommunication = [];

        void addMessage(ReadOnlySpan<byte> bytes) => receivedCommunication.Add(Encoding.ASCII.GetString(bytes));

        void addCommunicationChannels(ICommunicationChannel c)
        {
            c.DataReceived += addMessage;
            communicationChannels.Add(c);
        }

        void removeCommunicationChannels(ICommunicationChannel c)
        {
            c.DataReceived -= addMessage;
            communicationChannels.Remove(c);
        }

        udpCommunicationManager.CommunicationChannelEstablished += addCommunicationChannels;
        udpCommunicationManager.CommunicationChannelLost += removeCommunicationChannels;

        udpCommunicationManager.Start();

        UdpClient client = new UdpClient(12000, AddressFamily.InterNetwork);

        var task = client.ReceiveAsync();

        Assert.IsTrue(task.Wait(3000), "Client received poll message within 3 seconds");

        TestContext.WriteLine(Encoding.ASCII.GetString(task.Result.Buffer));
        TestContext.WriteLine($"{task.Result.RemoteEndPoint.Address}");

        Assert.IsTrue(Encoding.ASCII.GetString(task.Result.Buffer) == udpCommunicationManager.PollMessage,
            "Poll message was received intact");
        Assert.IsTrue(receivedCommunication.Count == 0, "Poll message didn't go through DataReceived");
        Assert.IsTrue(communicationChannels.Count == 0, "Server didn't establish any connections");

        client.Send("Response"u8, task.Result.RemoteEndPoint);


        Assert.IsTrue(waitUntilTrue(() => communicationChannels.Count == 1, 1),
            "Wait until server calls CommunicationChannelEstablished");

        Assert.IsTrue(waitUntilTrue(() => receivedCommunication.Count == 1, 1),
            "Response message went through DataReceived");

        TestContext.WriteLine(receivedCommunication[0]);

        Assert.IsTrue(waitUntilTrue(() => communicationChannels.Count == 0, 12),
            "Client disconnected after 10 seconds of inactivity");

        client.Send("Response"u8, task.Result.RemoteEndPoint);

        Assert.IsTrue(waitUntilTrue(() => communicationChannels.Count == 1, 1),
            "Client reconnected after client sends new pong");

        udpCommunicationManager.Stop();
        Assert.IsTrue(waitUntilTrue(() => communicationChannels.Count == 0, 2),
            "Client connected near immediately after communication manager stops");
    }
}
