namespace AutoFan.Hardware;

/// <summary>
/// Local lock file that means AUTO Fan currently holds software fan control.
/// The watchdog and the next launch restore BIOS control when this file exists.
/// </summary>
public sealed class SoftwareControlFlag
{
    public const string FileName = "software-control.active";

    public static string DefaultPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AUTO Fan",
        FileName);

    private readonly string _path;

    public SoftwareControlFlag()
        : this(DefaultPath)
    {
    }

    public SoftwareControlFlag(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public string Path => _path;

    public bool IsActive => File.Exists(_path);

    public void MarkActive()
    {
        string? directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_path, "active");
    }

    public void Clear()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
