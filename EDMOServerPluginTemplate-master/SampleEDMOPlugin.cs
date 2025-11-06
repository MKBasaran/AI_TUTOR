using ServerVNext.EDMO;
using ServerVNext.EDMO.Plugins;

namespace EDMOPluginTemplate;

public class SampleEDMOPlugin(EDMOSession session) : EDMOPlugin(session)
{
    public override string PluginName { get; } = nameof(SampleEDMOPlugin);

    public override void FrequencyChangedByUser(int userIndex, float freq)
    {
        SetFrequency(1.3f);
    }
}