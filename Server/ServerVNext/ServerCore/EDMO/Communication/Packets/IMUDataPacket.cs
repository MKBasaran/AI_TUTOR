using System.Numerics;
using System.Runtime.InteropServices;

namespace ServerCore.EDMO.Communication.Packets;

/// <summary>
/// The struct describing the layout of the IMU Data packet sent by the EDMO firmware.
/// </summary>
/// <param name="Data">The IMU data payload in the packet</param>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct IMUDataPacket(IMUData Data);

/// <summary>
/// A struct that contains readings for a particular sensor from the EDMO robots on-board IMU chip, along with some supporting metadata.
/// </summary>
/// <typeparam name="T">The data type of the reading</typeparam>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct SensorInfo<T> where T : unmanaged
{
    /// <summary>
    /// The time this information was recorded
    /// </summary>
    public required uint Timestamp { get; init; } // 4 bytes

    /// <summary>
    /// The accuracy values reported by the sensor
    /// </summary>
    /// <remarks>
    /// This can only have a value between <c>0</c> and <c>5</c> inclusive. Higher values indicate a higher confidence.
    /// </remarks>
    public required byte Accuracy { get; init; } // 1 byte

    // 3 Padding bytes

    /// <summary>
    /// The values recorded by the sensor
    /// </summary>
    public required T Data { get; init; } // sizeof(T)
}
/// <summary>
/// The struct containing the data recorded by the IMU data.
/// </summary>
/// /// <remarks>
/// If there is no IMU chip on-board the EDMO robot, the EDMO firmware will still provide IMUData, albeit with all sensor readings being 0.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct IMUData(
    SensorInfo<Vector3> Gyroscope,
    SensorInfo<Vector3> Accelerometer,
    SensorInfo<Vector3> MagneticField,
    SensorInfo<Vector3> Gravity,
    SensorInfo<Quaternion> Rotation
);
