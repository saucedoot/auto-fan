namespace AutoFan.Core;

public static class FanTestReasons
{
    public const string Pump = "Pump duty cycles are not written.";
    public const string AlreadyAtMaxDuty = "Already at maximum duty.";
    public const string NotControllable = "Fan group is not controllable.";
    public const string NoRpm = "No RPM reported; header may be empty.";
    public const string Gpu = "AMD and Intel GPU fans are not written.";
    public const string Coupled = "GPU fans on this card move together.";
    public const string GpuHeatInsufficient = "GPU Core did not heat enough to map GPU cooling.";
    public const string NoSpeedUp = "Duty or RPM did not increase enough to count as a fan test.";
    public const string CpuNotStable = "CPU temperatures were not steady enough to map CPU cooling.";
    public const string GpuNotStable = "GPU temperatures were not steady enough to map GPU cooling.";
    public const string Unsettled = "Temperatures were still moving at the end of the hold.";
}
