namespace Rebalancer.LeaseCoordination.Clients;

public enum ModifyClientResult
{
    Ok,
    FencingTokenViolation,
    Error,
}
