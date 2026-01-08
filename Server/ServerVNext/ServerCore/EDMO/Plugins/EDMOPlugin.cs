using System;
using System.Collections.Generic;
using ServerCore.EDMO.Communication.Packets;
using ServerCore.EDMO.Objectives;

namespace ServerCore.EDMO.Plugins;

/// <summary>
/// A type that serves as a base class for all plugins. Plugins can be used to extend the functionality of an <see cref="EDMOSession"/>.
/// <br/>
/// Plugins can observe data to provide analysis, manipulate oscillator parameters on behalf of users, or some other custom functionality.
/// </summary>
/// <param name="session">The host session</param>
public abstract class EDMOPlugin(EDMOSession session) : IDisposable
{
    /// <summary>
    /// The host session
    /// </summary>
    protected EDMOSession session = session;

    /// <summary>
    /// The name of this plugin
    /// </summary>

    public abstract string PluginName { get; }

    // Used to determine what plugins go first/last
    internal int Priority { get; set; }

    /// <summary>
    /// The <see cref="EDMOObjectiveGroup"/>s provided by this plugin.
    /// </summary>
    public EDMOObjectiveGroup[] ObjectiveGroups { get; protected set; } = [];

    /// <summary>
    /// Called when the session is created/started.
    /// </summary>
    public virtual void SessionStarted() { }

    /// <summary>
    /// Called when a user joins the session.
    /// </summary>
    /// <param name="index">The index of the user</param>
    /// <param name="userName">The name of the user</param>
    public virtual void UserJoined(int index, string userName) { }

    /// <summary>
    /// Called when a user leaves the session.
    /// </summary>
    /// <param name="index">The index of the user</param>
    /// <param name="userName">The name of the user</param>
    public virtual void UserLeft(int index, string userName) { }

    /// <summary>
    /// Called when IMU data is received from the EDMO robot.
    /// </summary>
    /// <param name="imuData">The imu data</param>
    public virtual void ImuDataReceived(IMUDataPacket imuData) { }

    /// <summary>
    /// Called when oscillator state data is received from the EDMO robot.
    /// </summary>
    /// <param name="oscillatorDataPacket">The current state of a single oscillator</param>
    public virtual void OscillatorDataReceived(OscillatorDataPacket oscillatorDataPacket) { }

    /// <summary>
    /// Called when a user changes the frequency of their oscillator.
    /// </summary>
    /// <param name="userIndex">The index of the user that changed the value</param>
    /// <param name="freq">The new frequency value</param>
    public virtual void FrequencyChangedByUser(int userIndex, float freq) { }

    /// <summary>
    /// Called when a user changes the amplitude  of their oscillator.
    /// </summary>
    /// <param name="userIndex">The index of the user that changed the value</param>
    /// <param name="amp">The new amplitude value</param>
    public virtual void AmplitudeChangedByUser(int userIndex, float amp) { }

    /// <summary>
    /// Called when a user changes the offset  of their oscillator.
    /// </summary>
    /// <param name="userIndex">The index of the user that changed the value</param>
    /// <param name="off">The new offset value</param>
    public virtual void OffsetChangedByUser(int userIndex, float off) { }

    /// <summary>
    /// Called when a user changes the phase shift  of their oscillator.
    /// </summary>
    /// <param name="userIndex">The index of the user that changed the value</param>
    /// <param name="phs">The new phase shift value</param>
    public virtual void PhaseShiftChangedByUser(int userIndex, float phs) { }

    internal virtual void FrequencyChangedByPlugin(EDMOPlugin plugin, float freq) { }
    internal virtual void AmplitudeChangedByPlugin(EDMOPlugin plugin, int index, float amp) { }
    internal virtual void OffsetChangedByPlugin(EDMOPlugin plugin, int index, float off) { }
    internal virtual void PhaseShiftChangedByPlugin(EDMOPlugin plugin, int index, float phs) { }

    /// <summary>
    /// Sets the frequency of <i>all</i> oscillators.
    /// </summary>
    /// <param name="freq">The target frequency.</param>
    protected void SetFrequency(float freq)
    {
        if (session.OscillatorParams[0].Frequency == freq)
            return;

        for (int i = 0; i < session.OscillatorParams.Length; ++i)
        {
            session.OscillatorParams[i].Frequency = freq;
            session.ConnectedUsers.GetValueOrDefault(i)?.ParamsUpdatedExternally?.Invoke();
        }

        foreach (var sessionPlugin in session.Plugins)
        {
            if (sessionPlugin == this)
                continue;

            sessionPlugin.FrequencyChangedByPlugin(this, freq);
        }
    }

    /// <summary>
    /// Sets the amplitude of the oscillator with a specific index.
    /// </summary>
    /// <param name="index">The index of the target oscillator</param>
    /// <param name="amp">The target amplitude value</param>
    protected void SetAmplitude(int index, float amp)
    {
        if (session.OscillatorParams[index].Amplitude == amp)
            return;

        session.OscillatorParams[index].Amplitude = amp;
        session.ConnectedUsers.GetValueOrDefault(index)?.ParamsUpdatedExternally?.Invoke();

        foreach (var sessionPlugin in session.Plugins)
        {
            if (sessionPlugin == this)
                continue;

            sessionPlugin.AmplitudeChangedByPlugin(this, index, amp);
        }
    }

    /// <summary>
    /// Sets the offset of the oscillator with a specific index.
    /// </summary>
    /// <param name="index">The index of the target oscillator</param>
    /// <param name="off">The target offset value</param>
    protected void SetOffset(int index, float off)
    {
        if (session.OscillatorParams[index].Offset == off)
            return;

        session.OscillatorParams[index].Offset = off;
        session.ConnectedUsers.GetValueOrDefault(index)?.ParamsUpdatedExternally?.Invoke();

        foreach (var sessionPlugin in session.Plugins)
        {
            if (sessionPlugin == this)
                continue;

            sessionPlugin.OffsetChangedByPlugin(this, index, off);
        }
    }
    /// <summary>
    /// Sets the phase shift of the oscillator with a specific index.
    /// </summary>
    /// <param name="index">The index of the target oscillator</param>
    /// <param name="phs">The target phase shift value</param>
    protected void SetPhaseShift(int index, float phs)
    {
        if (session.OscillatorParams[index].PhaseShift == phs)
            return;

        session.OscillatorParams[index].PhaseShift = phs;
        foreach (var userCtx in session.ConnectedUsers.Values)
        {
            userCtx.ExternalRelationChanged?.Invoke();
        }

        foreach (var sessionPlugin in session.Plugins)
        {
            if (sessionPlugin == this)
                continue;

            sessionPlugin.PhaseShiftChangedByPlugin(this, index, phs);
        }
    }

    public void SendFeedbackTo(int userIndex, string message)
    {
        session.ConnectedUsers[userIndex].FeedbackReceived?.Invoke(message);
    }

    /// <summary>
    /// Called when the EDMO robot is updated.
    /// </summary>
    /// <remarks>
    /// Use this as a way to perform periodic updates. If continuous updates are needed in parallel, consider using a background task.
    /// </remarks>
    public virtual void Update() { }

    /// <summary>
    /// Called when the session ends.
    /// </summary>
    /// <remarks>
    /// Use this to end plugin functionality and perform last moment procedures.
    /// </remarks>
    public virtual void SessionEnded() { }

    /// <summary>
    /// <inheritdoc cref="IDisposable.Dispose"/>
    /// </summary>
    /// <remarks>
    /// Avoid using this method to perform critical tasks. This method should only be used to perform resource cleanups.
    /// <br/>
    /// For last second jobs that should happen when the session ends, override <see cref="SessionEnded"/> instead.
    /// </remarks>
    protected virtual void Dispose(bool isDisposing) { }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
