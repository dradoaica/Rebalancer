namespace Rebalancer.SqlServer.Store;

using System.Collections.Generic;

internal class ResourceGroupStore
{
    private readonly object ResourceLockObj = new();
    private List<string> resources;

    public ResourceGroupStore()
    {
        this.resources = new List<string>();
        this.AssignmentStatus = AssignmentStatus.AssignmentInProgress;
    }

    public AssignmentStatus AssignmentStatus { get; set; }

    public GetResourcesResponse GetResources()
    {
        lock (this.ResourceLockObj)
        {
            if (this.AssignmentStatus == AssignmentStatus.ResourcesAssigned)
            {
                return new GetResourcesResponse
                {
                    Resources = new List<string>(this.resources), AssignmentStatus = this.AssignmentStatus
                };
            }

            return new GetResourcesResponse {Resources = new List<string>(), AssignmentStatus = this.AssignmentStatus};
        }
    }

    public void SetResources(SetResourcesRequest request)
    {
        lock (this.ResourceLockObj)
        {
            this.resources = new List<string>(request.Resources);
            this.AssignmentStatus = request.AssignmentStatus;
        }
    }
}
