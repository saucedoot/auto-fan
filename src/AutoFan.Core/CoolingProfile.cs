namespace AutoFan.Core;

public enum ProfileState
{
    Draft,
    Validated,
    EditedCheckRequired,
    StaleCheckRequired,
    Invalid,
}

public enum CurveSensor
{
    Cpu,
    Gpu,
}

public enum CurvePointOrigin
{
    SettledHold,
    MonotoneCorrection,
    SafetyExtension,
    DutyFloor,
}

public sealed record CurveSample(
    string GroupId,
    CurveSensor Sensor,
    double TemperatureCelsius,
    int DutyPercent,
    bool Settled);

public sealed record CurvePoint(
    string GroupId,
    CurveSensor Sensor,
    double TemperatureCelsius,
    int DutyPercent,
    MetricEvidence Evidence,
    CurvePointOrigin Origin);

/// <summary>
/// A saved temperature-to-duty draft. <see cref="ProfileState.Validated"/> is
/// not produced here.
/// </summary>
public sealed record CoolingProfile(
    Guid Id,
    DateTimeOffset CreatedAt,
    ProfileState State,
    string Detail,
    IReadOnlyList<CurvePoint> Points)
{
    public bool IsValidated => false;
}
