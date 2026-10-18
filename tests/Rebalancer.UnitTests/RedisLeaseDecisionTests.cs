using Rebalancer.Redis;
using Rebalancer.Redis.Leases;
using Xunit;

namespace Rebalancer.UnitTests;

public sealed class RedisLeaseDecisionTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Holder = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private static ResourceGroup Group(Guid coordinator, DateTime lastRenewal, int fencingToken = 5) =>
        new()
        {
            Name = "group",
            CoordinatorId = coordinator,
            LastCoordinatorRenewal = lastRenewal,
            FencingToken = fencingToken,
            LeaseExpirySeconds = 60,
            HeartbeatSeconds = 25,
        };

    [Fact]
    public void Acquire_IsDenied_WhileAnotherCoordinatorHoldsALiveLease()
    {
        var (newValue, response) = LeaseService.DecideAcquire(
            Group(Holder, Now.AddSeconds(-10)),
            new AcquireLeaseRequest
            {
                ClientId = Other,
                ResourceGroup = "group",
            },
            Now
        );

        Assert.Null(newValue);
        Assert.Equal(LeaseResult.Denied, response.Result);
    }

    [Fact]
    public void Acquire_IsGranted_WithNextFencingToken_WhenLeaseExpired()
    {
        var (newValue, response) = LeaseService.DecideAcquire(
            Group(Holder, Now.AddSeconds(-61)),
            new AcquireLeaseRequest
            {
                ClientId = Other,
                ResourceGroup = "group",
            },
            Now
        );

        Assert.Equal(LeaseResult.Granted, response.Result);
        Assert.Equal(6, response.Lease.FencingToken);
        Assert.Equal(Other, newValue.CoordinatorId);
        Assert.Equal(Now, newValue.LastCoordinatorRenewal);
    }

    [Fact]
    public void Acquire_ReturnsNoLease_WhenGroupDoesNotExist()
    {
        var (newValue, response) = LeaseService.DecideAcquire(
            null,
            new AcquireLeaseRequest
            {
                ClientId = Other,
                ResourceGroup = "group",
            },
            Now
        );

        Assert.Null(newValue);
        Assert.Equal(LeaseResult.NoLease, response.Result);
    }

    [Theory]
    [InlineData(false, 5, -10)] // another client is coordinator
    [InlineData(true, 4, -10)] // stale fencing token
    [InlineData(true, 5, -61)] // own lease already expired
    public void Renew_IsDenied_WithoutWriting(bool isHolder, int requestToken, int lastRenewalSeconds)
    {
        var (newValue, response) = LeaseService.DecideRenew(
            Group(isHolder ? Holder : Other, Now.AddSeconds(lastRenewalSeconds)),
            new RenewLeaseRequest
            {
                ClientId = Holder,
                ResourceGroup = "group",
                FencingToken = requestToken,
            },
            Now
        );

        Assert.Null(newValue);
        Assert.Equal(LeaseResult.Denied, response.Result);
    }

    [Fact]
    public void Renew_IsGranted_ForCurrentHolder()
    {
        var (newValue, response) = LeaseService.DecideRenew(
            Group(Holder, Now.AddSeconds(-10)),
            new RenewLeaseRequest
            {
                ClientId = Holder,
                ResourceGroup = "group",
                FencingToken = 5,
            },
            Now
        );

        Assert.Equal(LeaseResult.Granted, response.Result);
        Assert.Equal(5, response.Lease.FencingToken);
        Assert.Equal(Now, newValue.LastCoordinatorRenewal);
    }

    [Fact]
    public void Relinquish_DoesNotUnElect_ANewerCoordinator()
    {
        var (newValue, relinquished) = LeaseService.DecideRelinquish(
            Group(Other, Now, 6),
            new RelinquishLeaseRequest
            {
                ClientId = Holder,
                ResourceGroup = "group",
                FencingToken = 5,
            }
        );

        Assert.Null(newValue);
        Assert.False(relinquished);
    }

    [Fact]
    public void Relinquish_ClearsCoordinator_ForCurrentHolder()
    {
        var (newValue, relinquished) = LeaseService.DecideRelinquish(
            Group(Holder, Now),
            new RelinquishLeaseRequest
            {
                ClientId = Holder,
                ResourceGroup = "group",
                FencingToken = 5,
            }
        );

        Assert.True(relinquished);
        Assert.Equal(Guid.Empty, newValue.CoordinatorId);
    }
}
