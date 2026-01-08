# Session

This represents a single EDMO study session. This keeps track of the current oscillator parameters, current connected users that have control over the robot, and handles data collection from the EDMO robot.

Each session is bound to a [`FusedEDMOCommunication`](../Communication/connections.md#fused-edmo-communication), binding occurs during the start of a session. Upon (re)binding to a connection, the [`SessionStartPacket`](../Communication/fundamentals.md#session-start-packet) is sent, with the latest internal time received.

## User management
The number of users that can connect to a session is determined by the number of oscillators present on the robot. (As reported by [the identification packet](../Communication/fundamentals.md#identification-packet)

A user can attempt to connect to the session via the `EDMOSession.CreateContext(username)` method. If successful, this will return a context class that will contain essential information that describes the session from the point-of-view of the player.

> Note 1: If a session already has the max number of connected users/controllers, or is considered over, then `CreateContext()` will throw an `InvalidOperationException`.

Upon disposal of the context (via `context.Dispose()`) object, the session will be informed of the users departure, allowing for a reconnection or someone else to take over.

The session only ends once all users have left the session.

## Control context
Each control context represents a user's control/influence over the robot, and does not expose the full state of the session. It instead exposes information in a "need-to-know" basis for the controlling user. This information can be displayed or used to enhance the experience. The context also exposes getters/setters for the user's assigned oscillator, only allowing users to change their own oscillator's parameter.

> Frequency is a global parameter, a change made by one user will affect every user. This is designed intentionally to allow for power struggles within the group.

> Phase shift / Relation is publically viewable. This is because phase shift as a concept is harder to grasp an understand. Changing the phase shift only makes sense if done _relative_ to someone else.

## Objective management
Unlike the previous implementation, the session _does not_ perform management of objectives beyond exposing a read-only enumerable of objectives.

The tracking and management of objectives are relegated to plugins. This decision is made in order to keep the session object agnostic and more general-purpose. 

[Refer to this page for more details](../objectives.md)

## Data collection
Each session implicitly stores data related to the operation of the robot, the state of the session, and inputs made by each user. This is done via [FileLoggers](../../Loggers.md#file-logger), with a different file being created for each metric.

Each file is stored against the session root directory.
```
"{ExecutableDirectory}/Sessions/{dateTime:yyyyMMdd}/{Identifier}/{dateTime:HHmmss}"
```

The following are the list of files related to data collection:

|Log entry|Console|File|
|:---:|:---:|:---:|
|Session| ✅ | session.log|
|IMU data | ❎ | imu.log |
|Oscillator state (one file per oscillator) | ❎ | oscillator{i}_.log|
|User inputs (one file per user) | ❎ | user{i}.log|

[Plugins](#plugins) can augment the data collection procedure by recording additional data.

## Plugins
Sessions can be extended with [`EDMOPlugins`](../Plugins.md), providing extra functionality atop of the base foundations of the EDMO studies.

Plugins can hook into various parts of `EDMOSession`, including (but not limited to):

* User input
* IMU data received
* Oscillator state received
* User join/leave
* Session start/end
* Period updates

[Refer to the plugins documentation entry for further information](../Plugins.md)

## Special case consideration.

There are several considerations made for the reliability of the EDMO study session. Each of these have either been address via an implementation detail or simply ignored as "unlikely" to happen.

### Robot intermittent disconnection
In order to allow for a session to continue when a robot disconnects for a brief moment, a session will remain active as long as there are any users still in the session. 

When the connection to the robot is reestablish, it will be rebound to the active session.

The intention is that operators should never attempt to manually "reset" the robot through a reconnection, as the session will automatically synchronise and reset the robot when the session starts/ends.

### User disconnection
Under most circumstances, when a user disconnects involuntarily, the session will not be able to detect that. It is up to the server/frontend to provide a mitigation for that.

For the interfaces built for the studies, the client-side already have a mechanism to reconnect if it detects a small disconnection. On the server-side, a user is evicted if the connection is timed-out for a sufficiently long time.

This should be relatively uncommon if the network conditions are stable.
