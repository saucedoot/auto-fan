using AutoFan.Core;
using LibreHardwareMonitor.Hardware;

namespace AutoFan.Hardware;

/// <summary>
/// LibreHardwareMonitor backend. Reads this PC and may write motherboard /
/// Super I/O / controller PWM. NVIDIA GPU fans go through NVAPI. Every write
/// registers restore to BIOS and the NVIDIA driver curve.
/// </summary>
public sealed class LibreHardwareMonitorBackend : IHardwareBackend, IDisposable
{
    private readonly Computer? _computer;
    private readonly SoftwareControlLease _lease;
    private MappedHardware? _mapped;
    private string? _preferredGpuId;
    private bool _softwareControlActive;
    private bool _disposed;

    public LibreHardwareMonitorBackend()
        : this(new SoftwareControlLease())
    {
    }

    public LibreHardwareMonitorBackend(SoftwareControlLease lease)
    {
        _lease = lease ?? throw new ArgumentNullException(nameof(lease));

        try
        {
            var computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
            };
            computer.Open();
            _computer = computer;
        }
        catch (Exception exception)
        {
            OpenError = exception.Message;
            _computer = null;
        }
    }

    public string? OpenError { get; }

    public string DisplayName => Identity.DisplayName;

    public bool IsDemo => false;

    public bool HasActiveSoftwareControl => _softwareControlActive;

    public HardwareIdentity Identity => _mapped?.Identity ?? HardwareIdentity.Unknown;

    public IReadOnlyList<GpuDevice> Gpus => _mapped?.AvailableGpus ?? [];

    public void SetPreferredGpu(string? hardwareId)
    {
        _preferredGpuId = string.IsNullOrWhiteSpace(hardwareId) ? null : hardwareId;
    }

    public IReadOnlyList<FanGroup> FanGroups => ReadSnapshot().FanGroups;

    public HardwareSnapshot ReadSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_computer is null)
        {
            _mapped = new MappedHardware(
                new HardwareSnapshot(
                    DateTimeOffset.UtcNow,
                    Array.Empty<SensorReading>(),
                    Array.Empty<FanGroup>(),
                    IsDemoHardware: false),
                HardwareIdentity.Unknown);
            return _mapped.Snapshot;
        }

        _computer.Accept(new UpdateVisitor());
        _mapped = SensorTreeMapper.Map(
            SensorTreeReader.FromComputer(_computer),
            preferredGpuId: _preferredGpuId);
        return _mapped.Snapshot;
    }

    public DutySetResult TrySetDuty(string fanGroupId, int percent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(fanGroupId);

        if (_computer is null)
        {
            return new DutySetResult(false, OpenError ?? "Hardware is not available.");
        }

        if (!DutyPercent.TryCreate(percent, out DutyPercent duty))
        {
            return new DutySetResult(
                false,
                $"Duty cycle {percent}% is outside {SafetyLimits.MinDutyPercent}-{SafetyLimits.MaxDutyPercent}%.");
        }

        HardwareSnapshot snapshot = ReadSnapshot();
        ThermalAbortReason? abort = SafetyLimits.Evaluate(snapshot);
        if (abort is not null)
        {
            RestoreDefaults();
            return new DutySetResult(false, SafetyLimits.Describe(abort.Value));
        }

        FanGroup? group = snapshot.FanGroups.FirstOrDefault(item => item.Id == fanGroupId);
        if (group is null)
        {
            return new DutySetResult(false, $"Unknown fan group '{fanGroupId}'.");
        }

        if (group.Kind == FanGroupKind.Pump)
        {
            return new DutySetResult(false, FakeHardwareBackend.PumpRejectedMessage);
        }

        ControlLookup lookup = ControllableHeaders.FindControl(_computer, fanGroupId);
        if (!lookup.Found)
        {
            return new DutySetResult(false, $"Unknown fan group '{fanGroupId}'.");
        }

        if (lookup.HardwareType is HardwareType hardwareType
            && ControllableHeaders.IsGpuHardware(hardwareType))
        {
            if (hardwareType != HardwareType.GpuNvidia)
            {
                return new DutySetResult(false, FanTestReasons.Gpu);
            }

            _lease.OnTakingControl();
            DutySetResult nvidia = NvidiaFanWriter.TrySetDuty(Identity.GpuName, duty.Value);
            if (!nvidia.Accepted)
            {
                RestoreDefaults();
                return nvidia;
            }

            _softwareControlActive = true;
            return nvidia;
        }

        if (lookup.HardwareType is HardwareType type && !ControllableHeaders.IsWritableHardware(type))
        {
            return new DutySetResult(false, $"Fan group '{fanGroupId}' is not controllable.");
        }

        if (lookup.Control is not IControl control)
        {
            return new DutySetResult(false, $"Fan group '{fanGroupId}' is not controllable.");
        }

        _lease.OnTakingControl();
        try
        {
            control.SetSoftware(duty.Value);
        }
        catch (Exception exception)
        {
            RestoreDefaults();
            return new DutySetResult(false, exception.Message);
        }

        _softwareControlActive = true;
        return new DutySetResult(true, Error: null);
    }

    public void RestoreDefaults()
    {
        if (_disposed)
        {
            return;
        }

        if (_computer is not null)
        {
            try
            {
                _computer.Accept(new UpdateVisitor());
                foreach (IControl control in ControllableHeaders.WritableControls(_computer))
                {
                    try
                    {
                        control.SetDefault();
                    }
                    catch (Exception)
                    {
                        // Keep restoring the remaining headers.
                    }
                }
            }
            catch (Exception)
            {
                // Restore must not throw on the exit path.
            }
        }

        NvidiaFanWriter.RestoreAll();
        _softwareControlActive = false;
        _lease.OnRestored();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        RestoreDefaults();
        _computer?.Close();
        _disposed = true;
    }
}
