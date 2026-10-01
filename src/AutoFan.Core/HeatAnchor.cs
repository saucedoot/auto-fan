namespace AutoFan.Core;

public enum HeatAnchorKind
{
    Cpu,
    Gpu,
    Mixed,
}

/// <summary>
/// One frozen heat setting and whether that hold was actually steady.
/// A GPU rise near 15 °C does not make <see cref="IsMeasured"/> true.
/// </summary>
public sealed record HeatAnchor(
    HeatAnchorKind Kind,
    HeatProfile Profile,
    HoldAssessment Assessment,
    HardwareSnapshot? Reference,
    double? GpuRiseCelsius)
{
    public bool IsMeasured => Assessment == HoldAssessment.SettledMeasured;
}
