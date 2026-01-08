using System.IO;
using System.Linq;
using ServerCore.EDMO.Communication.Packets;
using ServerCore.Logging;

namespace ServerCore.EDMO.Plugins;

internal class LoggingPlugin : EDMOPlugin
{
    public override string PluginName => nameof(LoggingPlugin);

    private readonly CompositeLogger sessionLogger;
    private readonly FileLogger[] userLoggers;
    private readonly ILogger imuLogger;
    private readonly ILogger[] oscillatorLoggers;

    public LoggingPlugin(EDMOSession session) : base(session)
    {
        sessionLogger =
            new CompositeLogger
            {
                Loggers =
                [
                    new ConsoleLogger($"Session - {session.Identifier}"),
                    new FileLogger(
                        new FileInfo($"{session.SessionStorageDirectory}/session.log"), true
                    ),
                ]
            };

        imuLogger = new FileLogger(
            new FileInfo($"{session.SessionStorageDirectory}/imu.log"), true
        );

        oscillatorLoggers =
        [
            ..Enumerable.Range(0, 4)
                .Select(s => new FileLogger(
                    new FileInfo(
                        $"{session.SessionStorageDirectory}/oscillator{s}.log"), true
                ))
        ];

        userLoggers =
        [
            ..Enumerable.Range(0, 4)
                .Select(i => new FileLogger(
                    new FileInfo($"{session.SessionStorageDirectory}/User{i}.log"), true
                ))
        ];
    }

    public override void SessionStarted()
    {
        sessionLogger.Log($"Session for {session.Identifier} started.");
    }

    public override void UserJoined(int index, string userName)
    {
        userLoggers[index].Log($"{userName} is in control.");
        sessionLogger.Log($"{userName} joined session, assigned id {index}.");
    }

    public override void UserLeft(int index, string userName)
    {
        userLoggers[index].Log($"{userName} is no longer in control.");
        sessionLogger.Log($"{userName} left session, id {index} returned to pool.");
    }

    public override void ImuDataReceived(IMUDataPacket imuData)
    {
        imuLogger.Log(imuData);
    }

    public override void OscillatorDataReceived(OscillatorDataPacket oscillatorDataPacket)
    {
        oscillatorLoggers[oscillatorDataPacket.OscillatorIndex].Log(oscillatorDataPacket.OscillatorState);
    }

    public override void FrequencyChangedByUser(int userIndex, float freq)
    {
        sessionLogger.Log($"Frequency of all oscillators set to {freq} by user.");

        foreach (var userLogger in userLoggers)
            userLogger.Log($"User {userIndex} sets frequency to {freq}.");
    }

    public override void AmplitudeChangedByUser(int userIndex, float amp)
    {
        sessionLogger.Log($"Amplitude of oscillator {userIndex} set to {amp} by user.");
        userLoggers[userIndex].Log($"User sets amplitude to {amp}.");
    }

    public override void OffsetChangedByUser(int userIndex, float off)
    {
        sessionLogger.Log($"Offset of oscillator {userIndex} set to {off} by user.");
        userLoggers[userIndex].Log($"User sets offset to {off}.");
    }

    public override void PhaseShiftChangedByUser(int userIndex, float phs)
    {
        sessionLogger.Log($"Phase shift of oscillator {userIndex} set to {phs} by user.");
        userLoggers[userIndex].Log($"User sets phase shift to {phs}.");
    }

    internal override void FrequencyChangedByPlugin(EDMOPlugin plugin, float freq)
    {
        sessionLogger.Log($"Frequency of all oscillators set to {freq} by {plugin.PluginName}.");

        foreach (var userLogger in userLoggers)
            userLogger.Log($"{plugin.PluginName} sets frequency to {freq}.");
    }

    internal override void AmplitudeChangedByPlugin(EDMOPlugin plugin, int index, float amp)
    {
        sessionLogger.Log($"Amplitude of oscillator {index} set to {amp} by {plugin.PluginName}.");
        userLoggers[index].Log($"{plugin.PluginName} sets amplitude to {amp}.");
    }

    internal override void OffsetChangedByPlugin(EDMOPlugin plugin, int index, float off)
    {
        sessionLogger.Log($"Offset of oscillator {index} set to {off} by {plugin.PluginName}.");
        userLoggers[index].Log($"{plugin.PluginName} sets offset to {off}.");
    }

    internal override void PhaseShiftChangedByPlugin(EDMOPlugin plugin, int index, float phs)
    {
        sessionLogger.Log($"Phase shift of oscillator {index} set to {phs} by {plugin.PluginName}");
        userLoggers[index].Log($"User sets phase shift to {phs}.");
    }

    public override void SessionEnded()
    {
        sessionLogger.Log($"Session for {session.Identifier} ended.");
    }

    protected override void Dispose(bool isDisposing)
    {
        sessionLogger.Dispose();
        imuLogger.Dispose();

        foreach (var oscillatorLogger in oscillatorLoggers)
            oscillatorLogger.Dispose();

        foreach (var userLogger in userLoggers)
            userLogger.Dispose();

        base.Dispose(isDisposing);
    }
}
