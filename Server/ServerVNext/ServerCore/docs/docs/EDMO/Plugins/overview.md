# Overview

Plugins are the primary way to extend the functionality of [EDMO sessions](../Sessions/session.md). 

By using plugins, we maintain a high level of modularity between session functionality. We avoid bloating the session logic with "additional functionality". 

Plugins are dynamically loaded on runtime, and is intended to be developed "out-of-tree" from `ServerCore`. This allows for the ability to easily manage plugins or swap between alternative implementations. It also eases development of additional functionality (such as AI or peripheral integrations) without the need to interact with the main codebase.

Plugins can be developed/written in different format. Support of a format is determined by the existence of the corresponding PluginLoader.

> A plugin loader must be initialised with a plugin directory, where the plugins reside.

## Template
[A repository with a template can be found here](https://github.com/TeamEDMO/EDMOServerPluginTemplate)


## How it works

Plugins provide a set of method definition that will be called by the session upon specific events. Plugins can then respond accordingly based on their intended functionality. 

Plugins may manipulate the operation of the robot, by taking direct control of the oscillator control. This may be desired in the case of AI-powered assistance or something similar. Use of this should be done sparingly, as overuse may lead to the participants feeling like they are being robbed of control or being excessively handheld.

## Plugin loaders

Plugin loaders provide the functionality to interpret a plugin. This may include using an interop runtime, or dynamic loading of assemblies shipped separate from the main program.

Plugin loaders should ideally load the plugin definitions ahead, and if necessary, perform introspection to determine which methods a plugin provides.

A plugin loader may be passed to an `EDMOSessionManager`, which is then used to construct a fresh instance of each plugin for every new session.

By default there are two different `PluginLoader` implementations:

 * `DotnetPluginLoader` - A plugin loader that loads .NET `.dll` assemblies
 * `PythonPluginLoader` - Provides support for `.py` scripts via [PythonNet](https://pythonnet.github.io/)

> An additional `CompositePluginLoader` is also provided as a utility to combine multiple plugin loader into the same instance.


## Plugin functionality

An EDMO plugin must provide the plugin name, this is used internally by the runtime to ensure uniqueness.

### Events handlers
The following are the list of events that a plugin can subscribe to:

* `SessionStart` - Called when the session fully initialises, but just before robot communication is established
    + This is a good place to perform initial initialisation of the plugin, especially when it need to be appropriately timed to the start of the session
    + > Avoid performing timing dependent tasks in the constructor.
* `SessionEnded` - Called when the session ends, and all participants have left
    + Perform cleanup tasks here, releasing resources that may be used by the plugin. Along with performing work that needs to be timed to the ending of a session.
    + > **_DO NOT_** assume that your resources are released implicity by the runtime. There is no guarantee that the garbage collector will collect your resources in a timely fashion. (or at all)
* `UserJoined` - Called when a participant joins the session
* `UserLeft` - Called when a participant leaves the session
    + This may not be called immediately, as there may be cases where the session does not realise that a participant has left involuntarily
* `ImuDataReceived` - Called when IMU data is received from the robot.
    + > The event being called does not imply that new data is received, as the current readings is always sent by the robot implicitly. Rely on the `timestamp` of the sensor reading to determine "freshness" [See also: IMU packet format](../Communication/fundamentals.md#imu-data-packet)
* `OscillatorDataReceived` - Called when Oscillator state is received from the robot
* `FrequencyChangedByUser`
* `OffsetChangedByUser`
* `PhaseShiftChangedByUser`
* `Update` - A method called periodically based on the session "tick-rate", before updated parameters are sent to the robot.
    + Perform simple work here, or last minute updates to oscillator parameters.
    + > Do not perform long-running blocking work here. Doing so will block the session from performing timely updates to the robot (including sending updated oscillator parameters). Instead, consider using a background thread, and only synchronise during the update call. 

### Control methods
In addition to passive signals, there are a few methods provided to plugins in order to manipulate oscillator parameters.

* `SetFrequency` - Sets the global frequency for all oscillators
* `SetAmplitude` - Sets the amplitude for a specified oscillator
* `SetOffset` - Sets the offset for a specified oscillator
* `SetPhaseShift` - Sets the phase shift for a specified oscillator

### Existing plugins
* [LoggingPlugin](https://github.com/TeamEDMO/ServerCore/blob/master/ServerCore/EDMO/Plugins/LoggingPlugin.cs) - Provides basic logging capabilities to an EDMO session, recording all user/plugin manipulation of the oscillator, recording IMU and oscilator states. Uses some internal functionality. Written in C#.
* [GoProPlugin](https://github.com/TeamEDMO/GoProPlugin) - Plugin that provides GoPro integration to an EDMO study session, synchronising recordings of multiple cameras to the start and end of the session. Written in Python.
* [FileSyncPlugin](https://github.com/TeamEDMO/FileSyncPlugin) - Plugin that adds post-session synchronisation of recorded data with a recipient on the same network. Comes with a companion receiver program. Written in C#. 