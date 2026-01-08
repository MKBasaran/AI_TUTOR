using Python.Runtime;
using ServerCore.EDMO.Communication.Packets;
using ServerCore.EDMO.Objectives;

namespace ServerCore.EDMO.Plugins;

/// <summary>
/// This is a wrapper plugin, where the functionality is provided by a python dynamic type.
/// <br/>
/// Introspection is performed on the Python plugin to determine which methods are implemented.
/// This is done to avoid runtime errors when breaking changes happen, but also to avoid costly interop calls that can occur even with blank Python methods. ("Crossing the bridge")
/// <br/>
/// <br/>
/// <inheritdoc cref="EDMOPlugin"/>
/// </summary>
internal class EDMOPythonPlugin : EDMOPlugin
{
    public sealed override string PluginName { get; }

    private readonly PyObject pluginClass;
    private dynamic dynamicPluginClass => pluginClass;

    private readonly bool hasAmplitudeChanged;
    private readonly bool hasFrequencyChanged;
    private readonly bool hasGetName;
    private readonly bool hasImuDataReceived;
    private readonly bool hasOffsetChanged;
    private readonly bool hasOscDataReceived;
    private readonly bool hasPhaseShiftChanged;
    private readonly bool hasSessionEnded;
    private readonly bool hasSessionStarted;
    private readonly bool hasUserJoined;
    private readonly bool hasUserLeft;
    private readonly bool hasUpdate;


    public EDMOPythonPlugin(EDMOSession session, dynamic pythonCtor) : base(session)
    {
        using (Py.GIL())
        {
            pluginClass = pythonCtor(this);
            var pythonClassMembers = pluginClass.GetDynamicMemberNames();

            // Perform inspection to determine what methods are available
            foreach (string member in pythonClassMembers)
            {
                switch (member)
                {
                    case "amplitudeChanged":
                        hasAmplitudeChanged = true;
                        break;
                    case "frequencyChanged":
                        hasFrequencyChanged = true;
                        break;
                    case "getName":
                        hasGetName = true;
                        break;
                    case "imuDataReceived":
                        hasImuDataReceived = true;
                        break;
                    case "oscDataReceived":
                        hasOscDataReceived = true;
                        break;
                    case "offsetChanged":
                        hasOffsetChanged = true;
                        break;
                    case "phaseShiftChanged":
                        hasPhaseShiftChanged = true;
                        break;
                    case "sessionEnded":
                        hasSessionEnded = true;
                        break;
                    case "sessionStarted":
                        hasSessionStarted = true;
                        break;
                    case "userJoined":
                        hasUserJoined = true;
                        break;
                    case "userLeft":
                        hasUserLeft = true;
                        break;
                    case "update":
                        hasUpdate = true;
                        break;
                }
            }

            PluginName = hasGetName ? dynamicPluginClass.getName() : "PythonPlugin";
        }
    }

    public override void SessionStarted()
    {
        if (!hasSessionStarted)
            return;

        using (Py.GIL())
            dynamicPluginClass.sessionStarted();
    }

    public static EDMOObjectiveGroup CreateObjectiveGroup(string title, string? description = null!,
        params EDMOObjective[] objectives)
        => new(title, description, objectives);

    public static EDMOObjective CreateObjective(string title, string? description = null!) => new(title, description);

    public override void UserJoined(int index, string userName)
    {
        if (!hasUserJoined)
            return;

        using (Py.GIL())
            dynamicPluginClass.userJoined(index, userName);
    }

    public override void UserLeft(int index, string userName)
    {
        if (!hasUserLeft)
            return;

        using (Py.GIL())
            dynamicPluginClass.userLeft(index, userName);
    }

    public override void ImuDataReceived(IMUDataPacket imuData)
    {
        if (!hasImuDataReceived)
            return;

        using (Py.GIL())
            dynamicPluginClass.imuDataReceived(imuData);
    }

    public override void OscillatorDataReceived(OscillatorDataPacket oscillatorDataPacket)
    {
        if (!hasOscDataReceived)
            return;

        using (Py.GIL())
            dynamicPluginClass.oscDataReceived(oscillatorDataPacket);
    }

    public override void FrequencyChangedByUser(int userIndex, float freq)
    {
        if (!hasFrequencyChanged)
            return;

        using (Py.GIL())
            dynamicPluginClass.frequencyChanged(userIndex, freq);
    }

    public override void AmplitudeChangedByUser(int userIndex, float amp)
    {
        if (!hasAmplitudeChanged)
            return;

        using (Py.GIL())
            dynamicPluginClass.amplitudeChanged(userIndex, amp);
    }

    public override void OffsetChangedByUser(int userIndex, float off)
    {
        if (!hasOffsetChanged)
            return;

        using (Py.GIL())
            dynamicPluginClass.offsetChanged(userIndex, off);
    }

    public override void PhaseShiftChangedByUser(int userIndex, float phs)
    {
        if (!hasPhaseShiftChanged)
            return;

        using (Py.GIL())
            dynamicPluginClass.phaseShiftChanged(userIndex, phs);
    }

    public override void Update()
    {
        if (!hasUpdate)
            return;

        using (Py.GIL())
            dynamicPluginClass.update();
    }

    public override void SessionEnded()
    {
        if (!hasSessionEnded)
            return;

        using (Py.GIL())
            dynamicPluginClass.sessionEnded();
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);

        if (isDisposing)
            pluginClass?.Dispose();
    }
}
