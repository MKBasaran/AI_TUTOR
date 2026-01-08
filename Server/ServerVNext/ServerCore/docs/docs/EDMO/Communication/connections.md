# Connections
Within the EDMO studies, the robots communicate with the host via either serial, udp, or both simultaneously. As such, `EDMOCommunicationManager` is a composite `ICommunicationManager` that holds both `SerialCommunicationManager` and `UdpCommunicationManager`. Otherwise, `EDMOCommunicationManager` provides the same base expectations as other `ICommunicationManager`s.

## EDMO Connection
After establishing a communication channel via either serial or UDP, a validation check is done via `EDMOConnection`. 

Validation is performed by sending the identification command. Validation succeeds if the robot responds in the intended format within a set amount of time.

The connection also holds the identifier of the robot, along with the number of oscillators and the associated arm colours/hues. These properties are used by [Fused EDMO Connection](#fused-edmo-connection) and [EDMO sessions](../Sessions/session.md).

### Fused EDMO Connection

A robot can simultaneously communicate via serial and UDP channels. `FusedEDMOConnection` aims to consolidate all connections to the same robot, including a seamless fallback mechanism to the other established channel should the primary channel be lost.

There is also a mechanism to sort the connections within the fused connections, allowing for a priority system to be formed.

> If there is only a single established channel, a `FusedEDMOConnection` is still used, albeit with a single nested connection.

