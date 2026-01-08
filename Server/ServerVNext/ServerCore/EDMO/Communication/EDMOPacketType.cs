using ServerCore.EDMO.Communication.Commands;
using ServerCore.EDMO.Communication.Packets;

namespace ServerCore.EDMO.Communication;

/// <summary>
/// An enum that contains valid packet types that is supported by the EDMO firmware
/// </summary>
public enum EDMOPacketType : byte
{
    /// <summary>
    /// In command: Requests the EDMO robot to identify itself
    /// <br/>
    /// In response: Indicates the payload contains identification information. (<see cref="string"/>)
    /// </summary>
    Identify = 0,

    /// <summary>
    /// In command: Requests the EDMO robot to start pushing data, along with synchronizing with the provided timestamp. (<see cref="SessionStartCommand"/>)
    ///
    /// Will also hold a weak lock, preventing other servers from connecting.
    /// <br/>
    /// In response: N/A
    /// </summary>
    SessionStart = 1,

    /// <summary>
    /// In command: Requests the EDMO robot to send it's current time
    /// <br/>
    /// In response: Indicates the payload contains the local timestamp of the robot. (<see cref="TimePacket"/>)
    /// </summary>
    GetTime = 2,

    /// <summary>
    /// In command: Indicates that the payload contains parameters of a single oscillator, to be applied by the robot. (<see cref="UpdateOscillatorCommand"/>)
    /// <br/>
    /// In response: N/A
    /// </summary>
    UpdateOscillator = 3,

    /// <summary>
    /// In command: Requests the EDMO robot to send the current state of the oscillator.
    /// <br/>
    /// In response: Indicates that the payload contains the state of a single oscillator. (<see cref="OscillatorDataPacket"/>)
    /// </summary>
    SendMotorData = 4,

    /// <summary>
    /// In command: Requests the EDMO robot to send the current IMU state.
    /// <br/>
    /// In response: Indicates that the payload contains the current IMU readings. (<see cref="IMUDataPacket"/>)
    /// </summary>
    SendImuData = 5,

    /// <summary>
    /// In command: Requests the EDMO robot to stop pushing data, along with synchronizing with the provided timestamp. (<see cref="SessionStartCommand"/>)
    ///
    /// Will also release the lock held by this server. Allowing other servers to control the robot.
    /// <br/>
    /// In response: N/A
    /// </summary>
    SessionEnd = 6,

    /// <summary>
    /// In command: Requests the EDMO robot to send all data.
    /// <br/>
    /// In response: Indicates that the payload contains a batch of all information that it can send.
    /// </summary>
    /// <remarks>
    /// This is a operation that is a composite of <see cref="GetTime"/>, <see cref="SendMotorData"/> and <see cref="SendImuData"/>.
    /// </remarks>
    SendAllData = 69,
}
