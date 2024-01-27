﻿using System;
using System.Collections.Generic;

namespace Rebalancer.SqlServer.Clients;

public class ClientStartRequest
{
    public ClientStartRequest()
    {
        AssignedResources = new List<string>();
    }

    public Guid ClientId { get; set; }
    public List<string> AssignedResources { get; set; }
}
