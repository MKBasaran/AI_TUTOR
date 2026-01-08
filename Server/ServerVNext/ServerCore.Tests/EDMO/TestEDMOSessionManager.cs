using ServerCore.EDMO;

namespace ServerCore.Tests.EDMO;

[TestClass]
public class TestEDMOSessionManager
{
    private EDMOSessionManager sessionManager;

    [TestInitialize]
    private void initialise()
    {
        sessionManager = new();
        sessionManager.Start();
    }

    [TestCleanup]
    private void cleanup()
    {
        sessionManager.Stop();
    }


}
