namespace AutoFan.Core;

public enum ThermalAbortReason
{
    CpuOverLimit,
    GpuOverLimit,
    OtherSensorOverLimit,
    RateOfRise,
    TelemetryLost,
    GpuDeviceLost,
}
