namespace AutoFan.Core;

public sealed record InfluenceEntry(
    string FanGroupId,
    string FanGroupName,
    InfluenceTarget Target,
    double? DeltaCelsius,
    InfluenceEffect? Effect,
    MetricEvidence Evidence,
    int? DutyBefore,
    int? DutyAfter,
    double? RpmBefore,
    double? RpmAfter,
    string? SkipReason = null);
