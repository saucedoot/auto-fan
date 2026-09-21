using AutoFan.Core;

namespace AutoFan.Hardware;

internal sealed class CpuWorkload : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task[] _workers = [];
    private readonly object _gate = new();

    public void Set(WorkloadLevel level)
    {
        lock (_gate)
        {
            StartWorkers(WorkerCount(level));
        }
    }

    public void SetWorkers(int count)
    {
        lock (_gate)
        {
            StartWorkers(count);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopLocked();
        }
    }

    public void Dispose() => Stop();

    private void StopLocked()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();
        try
        {
            Task.WaitAll(_workers, TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _cts.Dispose();
        _cts = null;
        _workers = [];
    }

    private void StartWorkers(int count)
    {
        StopLocked();
        if (count <= 0)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        _workers = new Task[count];
        for (int index = 0; index < count; index++)
        {
            _workers[index] = Task.Factory.StartNew(
                () => Burn(token),
                token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
    }

    internal static int WorkerCount(WorkloadLevel level) =>
        level switch
        {
            WorkloadLevel.Everyday => HeatProfile.EverydayCpuWorkers,
            WorkloadLevel.Low => HeatProfile.EverydayCpuWorkers,
            WorkloadLevel.High => Math.Max(1, Environment.ProcessorCount),
            _ => 0,
        };

    private static void Burn(CancellationToken token)
    {
        double acc = 1.0;
        while (!token.IsCancellationRequested)
        {
            acc = Math.Sqrt((acc * 1_000_000_007d) + 1.0);
            if (acc > 1e12)
            {
                acc = 1.0;
            }
        }
    }
}
