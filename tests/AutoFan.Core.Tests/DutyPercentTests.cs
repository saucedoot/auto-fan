using AutoFan.Core;

namespace AutoFan.Core.Tests;

public sealed class DutyPercentTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public void TryCreate_accepts_in_range_values(int percent)
    {
        Assert.True(DutyPercent.TryCreate(percent, out DutyPercent duty));
        Assert.Equal(percent, duty.Value);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(150)]
    public void TryCreate_rejects_out_of_range_values(int percent)
    {
        Assert.False(DutyPercent.TryCreate(percent, out DutyPercent duty));
        Assert.Equal(0, duty.Value);
    }

    [Fact]
    public void Create_throws_for_out_of_range_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DutyPercent.Create(101));
        Assert.Throws<ArgumentOutOfRangeException>(() => DutyPercent.Create(-1));
    }
}
