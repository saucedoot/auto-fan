using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class GpuDeviceFilterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Microsoft Basic Render Driver")]
    [InlineData("WARP")]
    [InlineData("Software Adapter")]
    public void Rejects_missing_and_software_adapters(string? name)
    {
        Assert.True(GpuDeviceFilter.IsSoftwareAdapter(name));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070")]
    [InlineData("NVIDIA GeForce RTX 5070 Ti")]
    [InlineData("AMD Radeon RX 7800 XT")]
    [InlineData("Intel Arc A770")]
    public void Accepts_named_discrete_or_arc_devices(string name)
    {
        Assert.False(GpuDeviceFilter.IsSoftwareAdapter(name));
        Assert.True(GpuDeviceFilter.LooksDiscrete(name));
    }

    [Theory]
    [InlineData("Intel UHD Graphics")]
    [InlineData("Intel(R) Iris Xe Graphics")]
    [InlineData("AMD Radeon Graphics")]
    public void Rejects_integrated_graphics_as_discrete(string name)
    {
        Assert.False(GpuDeviceFilter.LooksDiscrete(name));
    }
}
