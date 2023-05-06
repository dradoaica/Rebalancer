namespace Rebalancer.Redis.Clients;

using System;
using System.Collections.Generic;

public class ClientStartRequest
{
    public ClientStartRequest() => this.AssignedResources = new List<string>();

    public Guid ClientId { get; set; }
    public List<string> AssignedResources { get; set; }
}
