using AutoFan.Core;
using AutoFan.Storage;

namespace AutoFan.Core.Tests;

public sealed class JsonUserSettingsStoreTests
{
    [Fact]
    public void Round_trip_keeps_slider_and_targets()
    {
        string path = Path.Combine(Path.GetTempPath(), $"autofan-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonUserSettingsStore(path);
            var original = new CoolingPreferences(0.25, 72, 68);

            store.Save(original);
            CoolingPreferences loaded = store.Load();

            Assert.Equal(0.25, loaded.Blend);
            Assert.Equal(72, loaded.CpuTarget);
            Assert.Equal(68, loaded.GpuTarget);
            Assert.Equal(SafetyLimits.CpuAbortCelsius, loaded.CpuAbort);
            Assert.Equal(SafetyLimits.GpuAbortCelsius, loaded.GpuAbort);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Missing_file_uses_defaults()
    {
        string path = Path.Combine(Path.GetTempPath(), $"autofan-settings-missing-{Guid.NewGuid():N}.json");
        var store = new JsonUserSettingsStore(path);

        CoolingPreferences loaded = store.Load();

        Assert.Equal(CoolingPreferences.DefaultQuietCool, loaded.Blend);
        Assert.Equal(CoolingPreferences.DefaultCpuTargetCelsius, loaded.CpuTarget);
        Assert.Equal(CoolingPreferences.DefaultGpuTargetCelsius, loaded.GpuTarget);
        Assert.Equal(SafetyLimits.CpuAbortCelsius, loaded.CpuAbort);
        Assert.Equal(SafetyLimits.GpuAbortCelsius, loaded.GpuAbort);
        Assert.Null(loaded.PreferredGpuId);
    }

    [Fact]
    public void Round_trip_keeps_abort_fields()
    {
        string path = Path.Combine(Path.GetTempPath(), $"autofan-settings-abort-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonUserSettingsStore(path);
            var original = new CoolingPreferences(0.25, 72, 68, 82, 75);

            store.Save(original);
            CoolingPreferences loaded = store.Load();

            Assert.Equal(82, loaded.CpuAbort);
            Assert.Equal(75, loaded.GpuAbort);
            Assert.Equal(72, loaded.CpuTarget);
            Assert.Equal(68, loaded.GpuTarget);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Round_trip_keeps_preferred_gpu()
    {
        string path = Path.Combine(Path.GetTempPath(), $"autofan-settings-gpu-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonUserSettingsStore(path);
            var original = new CoolingPreferences(0.25, 72, 68, PreferredGpuId: "/gpu/dgpu");

            store.Save(original);
            CoolingPreferences loaded = store.Load();

            Assert.Equal("/gpu/dgpu", loaded.PreferredGpuId);
            Assert.False(loaded.HideDisconnectedFans);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Old_settings_without_abort_keys_keep_the_floor()
    {
        string path = Path.Combine(Path.GetTempPath(), $"autofan-settings-old-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "QuietCool": 0.4,
                  "CpuTargetCelsius": 70,
                  "GpuTargetCelsius": 66
                }
                """);
            var store = new JsonUserSettingsStore(path);

            CoolingPreferences loaded = store.Load();

            Assert.Equal(0.4, loaded.Blend);
            Assert.Equal(70, loaded.CpuTarget);
            Assert.Equal(66, loaded.GpuTarget);
            Assert.Equal(SafetyLimits.CpuAbortCelsius, loaded.CpuAbort);
            Assert.Equal(SafetyLimits.GpuAbortCelsius, loaded.GpuAbort);
            Assert.Null(loaded.PreferredGpuId);
            Assert.False(loaded.HideDisconnectedFans);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Round_trip_keeps_hide_unused_fans()
    {
        string path = Path.Combine(Path.GetTempPath(), $"autofan-settings-unused-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonUserSettingsStore(path);
            var original = new CoolingPreferences(0.25, 72, 68, HideDisconnectedFans: true);

            store.Save(original);
            CoolingPreferences loaded = store.Load();

            Assert.True(loaded.HideDisconnectedFans);
            Assert.False(loaded.ConnectedFans.Completed);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Round_trip_keeps_connected_fan_presence()
    {
        string path = Path.Combine(Path.GetTempPath(), $"autofan-settings-presence-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonUserSettingsStore(path);
            var original = new CoolingPreferences(
                0.25,
                72,
                68,
                Presence: new FanPresence(true, ["/fan/used"], ["/fan/empty"]));

            store.Save(original);
            CoolingPreferences loaded = store.Load();

            Assert.True(loaded.ConnectedFans.Completed);
            Assert.Equal(["/fan/used"], loaded.ConnectedFans.ConnectedIds);
            Assert.Equal(["/fan/empty"], loaded.ConnectedFans.EmptyIds);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
