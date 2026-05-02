using ASCIIBot;
using ASCIIBot.Services;
using Microsoft.Extensions.Options;

namespace ASCIIBot.Tests;

public sealed class ConcurrencyGateTests
{
    private static ConcurrencyGate MakeGate(int maxGlobal = 3, int maxPerUser = 1)
    {
        var opts = Options.Create(new BotOptions
        {
            MaxGlobalJobs  = maxGlobal,
            MaxJobsPerUser = maxPerUser,
        });
        return new ConcurrencyGate(opts);
    }

    [Fact]
    public void TryAcquire_FirstRequest_ReturnsTrue()
    {
        var gate = MakeGate();
        Assert.True(gate.TryAcquire(1ul, out _, out _));
    }

    [Fact]
    public void TryAcquire_PerUserLimit_SecondRequestReturnsFalse()
    {
        var gate = MakeGate(maxGlobal: 3, maxPerUser: 1);
        Assert.True(gate.TryAcquire(1ul, out var handle1, out _));

        Assert.False(gate.TryAcquire(1ul, out _, out var rejection));
        Assert.Equal(ConcurrencyRejection.UserBusy, rejection);

        handle1.Dispose();
    }

    [Fact]
    public void TryAcquire_PerUserLimit_DifferentUsersAllowed()
    {
        var gate = MakeGate(maxGlobal: 3, maxPerUser: 1);
        Assert.True(gate.TryAcquire(1ul, out _, out _));
        Assert.True(gate.TryAcquire(2ul, out _, out _));
    }

    [Fact]
    public void TryAcquire_GlobalLimit_ExceedingCapacityReturnsFalse()
    {
        var gate = MakeGate(maxGlobal: 2, maxPerUser: 2);

        Assert.True(gate.TryAcquire(1ul, out var h1, out _));
        Assert.True(gate.TryAcquire(2ul, out var h2, out _));

        Assert.False(gate.TryAcquire(3ul, out _, out var rejection));
        Assert.Equal(ConcurrencyRejection.GlobalBusy, rejection);

        h1.Dispose();
        h2.Dispose();
    }

    [Fact]
    public void TryAcquire_AfterRelease_SlotBecomesAvailable()
    {
        var gate = MakeGate(maxGlobal: 1, maxPerUser: 1);
        Assert.True(gate.TryAcquire(1ul, out var handle, out _));
        Assert.False(gate.TryAcquire(2ul, out _, out _)); // global full

        handle.Dispose();

        Assert.True(gate.TryAcquire(2ul, out _, out _)); // now available
    }

    [Fact]
    public void ConcurrencyHandle_Dispose_ReleasesUserSlot()
    {
        var gate = MakeGate(maxGlobal: 3, maxPerUser: 1);
        Assert.True(gate.TryAcquire(1ul, out var handle, out _));
        Assert.False(gate.TryAcquire(1ul, out _, out _)); // per-user full

        handle.Dispose();

        Assert.True(gate.TryAcquire(1ul, out _, out _)); // slot released
    }
}
