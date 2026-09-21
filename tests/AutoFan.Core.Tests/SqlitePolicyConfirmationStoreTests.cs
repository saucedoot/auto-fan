using AutoFan.Core;
using AutoFan.Storage;

namespace AutoFan.Core.Tests;

public sealed class SqlitePolicyConfirmationStoreTests
{
    [Fact]
    public void Memory_round_trip_keeps_ambient_and_miss()
    {
        using var store = new SqlitePolicyConfirmationStore("Data Source=:memory:");
        var original = new PolicyConfirmation(
            78,
            72,
            74,
            70,
            Missed: true,
            AddedAirflow: true,
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            new DateTimeOffset(2026, 9, 19, 20, 0, 0, TimeSpan.Zero),
            27);

        store.Save(original);
        PolicyConfirmation? loaded = store.GetLatest();

        Assert.NotNull(loaded);
        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal(original.ObservedAt, loaded.ObservedAt);
        Assert.Equal(original.MeasuredCpuCelsius, loaded.MeasuredCpuCelsius);
        Assert.Equal(original.ExpectedGpuCelsius, loaded.ExpectedGpuCelsius);
        Assert.True(loaded.Missed);
        Assert.True(loaded.AddedAirflow);
        Assert.Equal(27, loaded.AmbientCelsius);
        Assert.Single(store.List());
    }
}
