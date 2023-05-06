namespace Rebalancer.Redis.Leases;

using System;

public class LeaseResponse
{
    public LeaseResult Result { get; set; }
    public Lease Lease { get; set; }
    public string Message { get; set; }
    public Exception Exception { get; set; }

    public bool IsErrorResponse() =>
        this.Result == LeaseResult.TransientError
        || this.Result == LeaseResult.Error;
}
