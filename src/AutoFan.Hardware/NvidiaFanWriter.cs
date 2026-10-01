using AutoFan.Core;
using NvAPIWrapper;
using NvAPIWrapper.GPU;

namespace AutoFan.Hardware;

/// <summary>
/// NVIDIA GPU fan writes through NVAPI. Restore returns driver automatic
/// control. Failures skip the group; they do not throw.
/// </summary>
public static class NvidiaFanWriter
{
    public const string Unavailable = "This NVIDIA driver cannot control GPU fans.";

    public static string? TryResolveId(string? preferredGpuName)
    {
        try
        {
            NVIDIA.Initialize();
            NvidiaGpuChoice[] choices = Choices();
            uint? id = NvidiaGpuSelector.Select(choices, hardwareId: null, preferredGpuName);
            return id?.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static DutySetResult TrySetDuty(string? preferredGpuName, int percent, string? gpuHardwareId = null)
    {
        if (!DutyPercent.TryCreate(percent, out DutyPercent duty))
        {
            return new DutySetResult(
                false,
                $"Duty cycle {percent}% is outside {SafetyLimits.MinDutyPercent}-{SafetyLimits.MaxDutyPercent}%.");
        }

        try
        {
            NVIDIA.Initialize();
            PhysicalGPU? gpu = Match(preferredGpuName, gpuHardwareId);
            if (gpu is null)
            {
                return new DutySetResult(false, Unavailable);
            }

            GPUCooler[] coolers = gpu.CoolerInformation.Coolers.ToArray();
            if (coolers.Length == 0)
            {
                return new DutySetResult(false, Unavailable);
            }

            foreach (GPUCooler cooler in coolers)
            {
                gpu.CoolerInformation.SetCoolerSettings(cooler.CoolerId, duty.Value);
            }

            return new DutySetResult(true, Error: null);
        }
        catch (Exception exception)
        {
            return new DutySetResult(
                false,
                string.IsNullOrWhiteSpace(exception.Message) ? Unavailable : exception.Message);
        }
    }

    public static void RestoreAll()
    {
        try
        {
            NVIDIA.Initialize();
            foreach (PhysicalGPU gpu in PhysicalGPU.GetPhysicalGPUs())
            {
                try
                {
                    gpu.CoolerInformation.RestoreCoolerSettingsToDefault();
                }
                catch (Exception)
                {
                    // Keep restoring remaining GPUs.
                }
            }
        }
        catch (Exception)
        {
            // Watchdog and exit restore must not throw.
        }
    }

    private static PhysicalGPU? Match(string? preferredGpuName, string? gpuHardwareId)
    {
        PhysicalGPU[] gpus = PhysicalGPU.GetPhysicalGPUs();
        uint? selected = NvidiaGpuSelector.Select(Choices(gpus), gpuHardwareId, preferredGpuName);
        if (selected is not uint id)
        {
            return null;
        }

        return gpus.FirstOrDefault(gpu => gpu.GPUId == id);
    }

    private static NvidiaGpuChoice[] Choices() => Choices(PhysicalGPU.GetPhysicalGPUs());

    private static NvidiaGpuChoice[] Choices(PhysicalGPU[] gpus) =>
        gpus.Select(gpu => new NvidiaGpuChoice(gpu.GPUId, gpu.FullName)).ToArray();
}
