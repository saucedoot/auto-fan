using System.Text.Json;
using AutoFan.Core;

namespace AutoFan.Storage;

public sealed class JsonUserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _path;

    public JsonUserSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public static JsonUserSettingsStore OpenLocalAppData()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AUTO Fan");
        Directory.CreateDirectory(directory);
        return new JsonUserSettingsStore(Path.Combine(directory, "settings.json"));
    }

    public CoolingPreferences Load()
    {
        if (!File.Exists(_path))
        {
            return CoolingPreferences.Default;
        }

        try
        {
            string json = File.ReadAllText(_path);
            UserSettingsDto? dto = JsonSerializer.Deserialize<UserSettingsDto>(json, JsonOptions);
            if (dto is null)
            {
                return CoolingPreferences.Default;
            }

            return new CoolingPreferences(
                dto.QuietCool,
                dto.CpuTargetCelsius,
                dto.GpuTargetCelsius,
                dto.CpuAbortCelsius,
                dto.GpuAbortCelsius,
                dto.PreferredGpuId,
                dto.HideDisconnectedFans,
                dto.FanPresenceCompleted
                    ? new FanPresence(
                        true,
                        dto.ConnectedFanGroupIds ?? [],
                        dto.EmptyFanGroupIds ?? [])
                    : FanPresence.None);
        }
        catch (JsonException)
        {
            return CoolingPreferences.Default;
        }
        catch (IOException)
        {
            return CoolingPreferences.Default;
        }
    }

    public void Save(CoolingPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        FanPresence presence = preferences.ConnectedFans;
        var dto = new UserSettingsDto(
            preferences.Blend,
            preferences.CpuTarget,
            preferences.GpuTarget,
            preferences.CpuAbort,
            preferences.GpuAbort,
            preferences.PreferredGpuId,
            preferences.HideDisconnectedFans,
            presence.Completed,
            presence.ConnectedIds.ToArray(),
            presence.EmptyIds.ToArray());
        File.WriteAllText(_path, JsonSerializer.Serialize(dto, JsonOptions));
    }

    private sealed record UserSettingsDto(
        double QuietCool,
        double CpuTargetCelsius,
        double GpuTargetCelsius,
        double? CpuAbortCelsius = null,
        double? GpuAbortCelsius = null,
        string? PreferredGpuId = null,
        bool HideDisconnectedFans = false,
        bool FanPresenceCompleted = false,
        string[]? ConnectedFanGroupIds = null,
        string[]? EmptyFanGroupIds = null);
}
