﻿using System;

namespace Rebalancer.Redis.Leases;

public class RenewLeaseRequest
{
    public Guid ClientId { get; set; }
    public string ResourceGroup { get; set; }
    public int FencingToken { get; set; }
}
