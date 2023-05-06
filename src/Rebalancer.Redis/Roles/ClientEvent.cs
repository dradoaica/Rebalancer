﻿namespace Rebalancer.Redis.Roles;

using System;

public class ClientEvent
{
    public EventType EventType { get; set; }
    public int FencingToken { get; set; }
    public string ResourceGroup { get; set; }
    public TimeSpan KeepAliveExpiryPeriod { get; set; }
    public CoordinatorToken CoordinatorToken { get; set; }
}
