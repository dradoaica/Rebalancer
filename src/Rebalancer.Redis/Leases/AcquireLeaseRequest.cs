﻿namespace Rebalancer.Redis.Leases;

using System;

public class AcquireLeaseRequest
{
    public Guid ClientId { get; set; }
    public string ResourceGroup { get; set; }
}