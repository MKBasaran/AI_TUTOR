using System;
using System.Collections.Generic;
using System.Linq;
using ServerCore.EDMO.Communication.Commands;
using ServerCore.EDMO.Communication.Packets;
using ServerCore.EDMO.Objectives;

namespace ServerCore.EDMO;

public partial class EDMOSession
{
    /// <summary>
    /// A context that provides methods to acquire information about a session, and methods to control an oscillator.
    /// </summary>
    public sealed class ControlContext : IDisposable
    {
        /// <inheritdoc cref="ControlContext"/>
        /// <param name="session">The session this context is tied to</param>
        /// <param name="index">The index of the oscillator to be controlled through this context</param>
        /// <param name="identifier">The identifier of this context, usually the name of the user controlling this.</param>
        public ControlContext(EDMOSession session, int index, string identifier)
        {
            this.session = session;
            Index = index;
            ControllerIdentifier = identifier;
        }

        private readonly EDMOSession session;

        /// <summary>
        /// The index of the oscillator to be controlled through this context
        /// </summary>
        public readonly int Index;

        /// <summary>
        /// The identifier of this context, usually the name of the user controlling this.
        /// </summary>
        public readonly string ControllerIdentifier;

        /// <summary>
        /// Gets the colour hue of a specific oscillator.
        /// </summary>
        /// <param name="oscillatorIndex">The target oscillator's index</param>
        /// <returns>A hue value in the range of [0,360)</returns>
        public ushort AssignedHueOf(int oscillatorIndex) => session.hues[oscillatorIndex];

        /// <summary>
        /// An enumerator containing all objective groups in this session.
        /// </summary>
        public IEnumerable<EDMOObjectiveGroup> ObjectiveGroups => session.Plugins.SelectMany(p => p.ObjectiveGroups);

        /// <summary>
        /// An event that is raised when the feedback is received from an external source. This can be displayed to the user.
        /// </summary>
        public Action<string>? FeedbackReceived { get; set; }

        /// <summary>
        /// An event that is raised when parameters for this context's oscillator is modified externally, requiring revalidation by the consumer.
        /// </summary>
        public Action? ParamsUpdatedExternally { get; set; }

        /// <summary>
        /// An event that is raised when the phase shift of another oscillator is updated, requiring potential revalidation.
        /// </summary>
        public Action? ExternalRelationChanged { get; set; }

        /// <summary>
        /// An event that is raised when a packet associated with this context's oscillator is received.
        /// </summary>
        public Action<OscillatorDataPacket>? OscillatorDataReceived { get; set; }

        /// <summary>
        /// The list of users, along with their index.
        /// </summary>
        public IEnumerable<(int Index, string Name)> Players =>
            session.ConnectedUsers.Select(kvp => (kvp.Key, kvp.Value.ControllerIdentifier));

        /// <summary>
        /// An even that is raised when the player list is changed.
        /// </summary>
        public Action? PlayerListUpdated { get; set; }

        /// <summary>
        /// The list of parameters used by all oscillators in the session.
        /// </summary>
        public OscillatorParams[] OscillatorParams => session.OscillatorParams;

        private ref OscillatorParams targetOscillator => ref session.OscillatorParams[Index];

        /// <summary>
        /// Get/Set the amplitude of the oscillator associated with this context
        /// </summary>
        public float Amplitude
        {
            get => targetOscillator.Amplitude;
            set
            {
                if (targetOscillator.Amplitude == value)
                    return;

                foreach (var plugin in session.Plugins)
                    plugin.AmplitudeChangedByUser(Index, value);

                targetOscillator.Amplitude = value;
            }
        }

        /// <summary>
        /// Get/Set the frequency of the oscillator associated with this context
        /// </summary>
        public float Frequency
        {
            get => targetOscillator.Frequency;
            set
            {
                if (targetOscillator.Frequency == value)
                    return;

                foreach (var plugin in session.Plugins)
                    plugin.FrequencyChangedByUser(Index, value);

                for (int i = 0; i < OscillatorParams.Length; ++i)
                    OscillatorParams[i].Frequency = value;
                foreach (var user in session.ConnectedUsers)
                    user.Value.ParamsUpdatedExternally?.Invoke();
            }
        }

        /// <summary>
        /// Get/Set the offset of the oscillator associated with this context
        /// </summary>
        public float Offset
        {
            get => targetOscillator.Offset;
            set
            {
                if (targetOscillator.Offset == value)
                    return;

                foreach (var plugin in session.Plugins)
                    plugin.OffsetChangedByUser(Index, value);

                targetOscillator.Offset = value;
            }
        }

        /// <summary>
        /// Get/Set the phase shift of the oscillator associated with this context
        /// </summary>
        public float PhaseShift
        {
            get => targetOscillator.PhaseShift;
            set
            {
                if (targetOscillator.PhaseShift == value)
                    return;

                foreach (var plugin in session.Plugins)
                    plugin.PhaseShiftChangedByUser(Index, value);

                foreach (var user in session.ConnectedUsers)
                {
                    if (user.Value == this)
                        continue;
                    user.Value.ExternalRelationChanged?.Invoke();
                }

                targetOscillator.PhaseShift = value;
            }
        }

        /// <summary>
        /// Reset the parameters of the oscillator associated with this context to their default values.
        /// </summary>
        public void ResetValues()
        {
            Amplitude = 0;
            Frequency = 0;
            PhaseShift = 0;
            Offset = 90;
        }

        private bool isDisposed;

        /// <inheritdoc/>
        public void Dispose()
        {
            if (isDisposed)
                return;

            isDisposed = true;
            session.closeContext(this);
        }
    }
}
