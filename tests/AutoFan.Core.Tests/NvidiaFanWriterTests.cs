using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class NvidiaFanWriterTests
{
    [Fact]
    public void RestoreAll_does_not_throw_when_nvapi_is_missing()
    {
        NvidiaFanWriter.RestoreAll();
    }
}
