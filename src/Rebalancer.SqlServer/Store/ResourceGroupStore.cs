using System.Collections.Generic;

namespace Rebalancer.SqlServer.Store;

internal class ResourceGroupStore
{
    private readonly object ResourceLockObj = new();
    private List<string> resources;

    public ResourceGroupStore()
    {
        resources = new List<string>();
        AssignmentStatus = AssignmentStatus.AssignmentInProgress;
    }

    public AssignmentStatus AssignmentStatus { get; set; }

    public GetResourcesResponse GetResources()
    {
        lock (ResourceLockObj)
        {
            if (AssignmentStatus == AssignmentStatus.ResourcesAssigned)
            {
                return new GetResourcesResponse
                {
                    Resources = new List<string>(resources), AssignmentStatus = AssignmentStatus,
                };
            }

            return new GetResourcesResponse { Resources = new List<string>(), AssignmentStatus = AssignmentStatus };
        }
    }

    public void SetResources(SetResourcesRequest request)
    {
        lock (ResourceLockObj)
        {
            resources = new List<string>(request.Resources);
            AssignmentStatus = request.AssignmentStatus;
        }
    }
}
