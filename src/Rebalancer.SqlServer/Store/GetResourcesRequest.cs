﻿using System.Collections.Generic;

namespace Rebalancer.SqlServer.Store;

internal class GetResourcesRequest
{
    public AssignmentStatus ResourceAssignmentStatus { get; set; }
    public List<string> Resources { get; set; }
}
