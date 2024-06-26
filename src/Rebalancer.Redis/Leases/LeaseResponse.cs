﻿using System;

namespace Rebalancer.Redis.Leases;

public class LeaseResponse
{
    public LeaseResult Result { get; set; }
    public Lease Lease { get; set; }
    public string Message { get; set; }
    public Exception Exception { get; set; }

    public bool IsErrorResponse() => Result == LeaseResult.TransientError || Result == LeaseResult.Error;
}
