using GB = Rebalancer.ZooKeeper.GlobalBarrier;
using RB = Rebalancer.ZooKeeper.ResourceBarrier;

namespace Rebalancer.ZooKeeper;

using System;
using System.Threading;
using System.Threading.Tasks;
using Core;
using Core.Logging;
using ResourceManagement;
using Zk;

public class ZooKeeperProvider : IRebalancerProvider
{
    private readonly TimeSpan connectTimeout;

    // services
    private readonly IRebalancerLogger logger;
    private readonly TimeSpan minimumRebalancingInterval;
    private readonly Random rand;

    // non-mutable state
    private readonly RebalancingMode rebalancingMode;
    private readonly TimeSpan sessionTimeout;
    private readonly object startLockObj = new();
    private readonly string zooKeeperRootPath;
    private readonly IZooKeeperService zooKeeperService;
    private bool aborted;

    // mutable state
    private string clientId;
    private int clientNumber;
    private string clientPath;
    private int epoch;
    private Task mainTask;
    private TimeSpan onStartDelay;
    private string resourceGroup;
    private ResourceManager resourceManager;
    private bool started;
    private ClientInternalState state;
    private string watchSiblingNodePath;

    public ZooKeeperProvider(string zookeeperHosts,
        string zooKeeperRootPath,
        TimeSpan sessionTimeout,
        TimeSpan connectTimeout,
        TimeSpan minimumRebalancingInterval,
        RebalancingMode rebalancingMode,
        IRebalancerLogger logger,
        IZooKeeperService zooKeeperService = null)
    {
        this.zooKeeperRootPath = zooKeeperRootPath;
        this.rebalancingMode = rebalancingMode;
        this.logger = logger;
        this.sessionTimeout = sessionTimeout;
        this.connectTimeout = connectTimeout;
        this.minimumRebalancingInterval = minimumRebalancingInterval;
        this.rand = new Random(Guid.NewGuid().GetHashCode());

        if (zooKeeperService == null)
        {
            this.zooKeeperService = new ZooKeeperService(zookeeperHosts);
        }
        else
        {
            this.zooKeeperService = zooKeeperService;
        }
    }

    public async Task StartAsync(string resourceGroup,
        OnChangeActions onChangeActions,
        CancellationToken token,
        ClientOptions clientOptions)
    {
        // just in case someone tries to start the client twice (with some concurrency)
        lock (this.startLockObj)
        {
            if (this.started)
            {
                throw new RebalancerException("Client already started");
            }

            this.started = true;
        }

        this.resourceManager =
            new ResourceManager(this.zooKeeperService, this.logger, onChangeActions, this.rebalancingMode);
        this.SetStateToNoSession();
        this.resourceGroup = resourceGroup;
        this.onStartDelay = clientOptions.OnAssignmentDelay;

        this.mainTask = Task.Run(async () => await this.RunStateMachine(token, clientOptions));
        await Task.Yield();
    }

    public async Task RecreateClientAsync()
    {
        if (!this.started)
        {
            throw new RebalancerException("Client not started");
        }

        await this.CreateClientNodeAsync();
    }

    public async Task WaitForCompletionAsync() => await this.mainTask;

    public AssignedResources GetAssignedResources()
    {
        var assignment = this.resourceManager.GetResources();
        return new AssignedResources
        {
            Resources = assignment.Resources, ClientState = this.GetState(assignment.AssignmentStatus)
        };
    }

    public ClientState GetState()
    {
        if (this.started)
        {
            var assignmentState = this.resourceManager.GetAssignmentStatus();
            return this.GetState(assignmentState);
        }

        if (this.aborted)
        {
            return ClientState.Aborted;
        }

        return ClientState.NotStarted;
    }

    private async Task RunStateMachine(CancellationToken token, ClientOptions clientOptions)
    {
        while (this.state != ClientInternalState.Terminated)
        {
            if (token.IsCancellationRequested)
            {
                await this.TerminateAsync("cancellation", false);
            }

            try
            {
                switch (this.state)
                {
                    case ClientInternalState.NoSession:
                        var established = await this.EstablishSessionAsync(token);
                        switch (established)
                        {
                            case NewSessionResult.Established:
                                this.state = ClientInternalState.NoClientNode;
                                break;
                            case NewSessionResult.TimeOut:
                                this.state = ClientInternalState.NoSession;
                                await this.WaitRandomTime(TimeSpan.FromSeconds(5));
                                break;
                            default:
                                this.state = ClientInternalState.Error;
                                break;
                        }

                        break;
                    case ClientInternalState.NoClientNode:
                        var created = await this.CreateClientNodeAsync();
                        if (created)
                        {
                            this.state = ClientInternalState.NoRole;
                        }
                        else
                        {
                            this.state = ClientInternalState.Error;
                        }

                        break;
                    case ClientInternalState.NoRole:
                        var epochAttained = await this.CacheEpochLocallyAsync();
                        if (!epochAttained)
                        {
                            await this.EvaluateTerminationAsync(token, clientOptions,
                                "Couldn't read the current epoch.");
                        }

                        (var electionResult, var lowerSiblingPath) = await this.DetermineLeadershipAsync();
                        switch (electionResult)
                        {
                            case ElectionResult.IsLeader:
                                this.state = ClientInternalState.IsLeader;
                                this.watchSiblingNodePath = string.Empty;
                                break;
                            case ElectionResult.IsFollower:
                                this.state = ClientInternalState.IsFollower;
                                this.watchSiblingNodePath = lowerSiblingPath;
                                break;
                            default:
                                await this.EvaluateTerminationAsync(token, clientOptions,
                                    "The client has entered an unknown state");
                                break;
                        }

                        break;
                    case ClientInternalState.IsLeader:
                        var coordinatorExitReason = await this.BecomeCoordinatorAsync(token);
                        switch (coordinatorExitReason)
                        {
                            case CoordinatorExitReason.NoLongerCoordinator:
                                this.SetStateToNoSession(); // need a new client node
                                break;
                            case CoordinatorExitReason.Cancelled:
                                await this.TerminateAsync("cancellation", false);
                                break;
                            case CoordinatorExitReason.SessionExpired:
                                this.SetStateToNoSession();
                                break;
                            case CoordinatorExitReason.PotentialInconsistentState:
                                await this.EvaluateTerminationAsync(token, clientOptions,
                                    "The client has entered a potentially inconsistent state");
                                break;
                            case CoordinatorExitReason.FatalError:
                                await this.TerminateAsync("fatal error", true);
                                break;
                            default:
                                await this.EvaluateTerminationAsync(token, clientOptions,
                                    "The client has entered an unknown state");
                                break;
                        }

                        break;
                    case ClientInternalState.IsFollower:
                        var followerExitReason = await this.BecomeFollowerAsync(token);
                        switch (followerExitReason)
                        {
                            case FollowerExitReason.PossibleRoleChange:
                                this.state = ClientInternalState.NoRole;
                                break;
                            case FollowerExitReason.Cancelled:
                                await this.TerminateAsync("cancellation", false);
                                break;
                            case FollowerExitReason.SessionExpired:
                                this.SetStateToNoSession();
                                break;
                            case FollowerExitReason.FatalError:
                                await this.TerminateAsync("fatal error", true);
                                break;
                            case FollowerExitReason.PotentialInconsistentState:
                                await this.EvaluateTerminationAsync(token, clientOptions,
                                    "The client has entered an potential inconsistent state");
                                break;
                            default:
                                await this.EvaluateTerminationAsync(token, clientOptions,
                                    "The client has entered an unknown state");
                                break;
                        }

                        break;
                    case ClientInternalState.Error:
                        await this.EvaluateTerminationAsync(token, clientOptions,
                            "The client has entered an error state");
                        break;
                    default:
                        await this.EvaluateTerminationAsync(token, clientOptions,
                            "The client has entered an unknown state");
                        break;
                }
            }
            catch (ZkSessionExpiredException)
            {
                this.logger.Info(this.clientId, "ZooKeeper session lost");
                this.SetStateToNoSession();
            }
            catch (ZkOperationCancelledException)
            {
                await this.TerminateAsync("cancellation", false);
            }
            catch (TerminateClientException e)
            {
                await this.TerminateAsync("Fatal error", true, e);
            }
            catch (InconsistentStateException e)
            {
                await this.EvaluateTerminationAsync(token, clientOptions,
                    "An error has caused that may have left the client in an inconsistent state.", e);
            }
            catch (Exception e)
            {
                await this.EvaluateTerminationAsync(token, clientOptions, "An unexpected error has been caught", e);
            }
        }
    }

    private ClientState GetState(AssignmentStatus assignmentState)
    {
        switch (assignmentState)
        {
            case AssignmentStatus.ResourcesAssigned:
            case AssignmentStatus.NoResourcesAssigned:
                return ClientState.Assigned;
            case AssignmentStatus.NoAssignmentYet:
                return ClientState.PendingAssignment;
            default:
                return ClientState.PendingAssignment;
        }
    }

    private void ResetMutableState()
    {
        this.clientId = string.Empty;
        this.clientNumber = -1;
        this.clientPath = string.Empty;
        this.watchSiblingNodePath = string.Empty;
        this.epoch = 0;
    }

    private async Task EvaluateTerminationAsync(CancellationToken token,
        ClientOptions clientOptions,
        string message,
        Exception e = null)
    {
        if (token.IsCancellationRequested)
        {
            await this.TerminateAsync("cancellation", false);
        }
        else if (clientOptions.AutoRecoveryOnError)
        {
            this.SetStateToNoSession();
            if (e != null)
            {
                this.logger.Error(this.clientId,
                    $"Error: {message} - {e} Auto-recovery enabled. Will restart in {clientOptions.RestartDelay.TotalMilliseconds}ms.");
            }
            else
            {
                this.logger.Error(this.clientId,
                    $"Error: {message} Auto-recovery enabled. Will restart in {clientOptions.RestartDelay.TotalMilliseconds}ms.");
            }

            await Task.Delay(clientOptions.RestartDelay);
        }
        else
        {
            await this.TerminateAsync($"Error: {message}. Auto-recovery disabled", true, e);
        }
    }

    private void SetStateToNoSession()
    {
        this.ResetMutableState();
        this.state = ClientInternalState.NoSession;
    }

    private async Task<NewSessionResult> EstablishSessionAsync(CancellationToken token)
    {
        var randomWait = this.rand.Next(2000);
        this.logger.Info(this.clientId, $"Will try to open a new session in {randomWait}ms");
        await Task.Delay(randomWait);

        // blocks until the session starts or timesout
        var connected = await this.zooKeeperService.StartSessionAsync(this.sessionTimeout, this.connectTimeout, token);
        if (!connected)
        {
            this.logger.Error(this.clientId, "Failed to open a session, connect timeout exceeded");
            return NewSessionResult.TimeOut;
        }

        this.logger.Info(this.clientId, "Initializing zookeeper client paths");

        try
        {
            switch (this.rebalancingMode)
            {
                case RebalancingMode.GlobalBarrier:
                    await this.zooKeeperService.InitializeGlobalBarrierAsync(
                        $"{this.zooKeeperRootPath}/{this.resourceGroup}/clients",
                        $"{this.zooKeeperRootPath}/{this.resourceGroup}/status",
                        $"{this.zooKeeperRootPath}/{this.resourceGroup}/stopped",
                        $"{this.zooKeeperRootPath}/{this.resourceGroup}/resources",
                        $"{this.zooKeeperRootPath}/{this.resourceGroup}/epoch");
                    break;
                case RebalancingMode.ResourceBarrier:
                    await this.zooKeeperService.InitializeResourceBarrierAsync(
                        $"{this.zooKeeperRootPath}/{this.resourceGroup}/clients",
                        $"{this.zooKeeperRootPath}/{this.resourceGroup}/resources",
                        $"{this.zooKeeperRootPath}/{this.resourceGroup}/epoch");
                    break;
            }
        }
        catch (ZkInvalidOperationException e)
        {
            var msg =
                "Could not start a new rebalancer client due to a problem with the prerequisite paths in ZooKeeper.";
            this.logger.Error(this.clientId, msg, e);
            return NewSessionResult.Error;
        }
        catch (Exception e)
        {
            var msg =
                "An unexpected error occurred while intializing the rebalancer ZooKeeper paths";
            this.logger.Error(this.clientId, msg, e);
            return NewSessionResult.Error;
        }

        return NewSessionResult.Established;
    }

    private async Task<bool> CreateClientNodeAsync()
    {
        try
        {
            this.clientPath = await this.zooKeeperService.CreateClientAsync();
            this.SetIdFromPath();
            this.logger.Info(this.clientId, $"Client znode registered with Id {this.clientId}");
            return true;
        }
        catch (ZkInvalidOperationException e)
        {
            this.logger.Error(this.clientId, "Could not create the client znode.", e);
            return false;
        }
    }

    private void SetIdFromPath()
    {
        this.clientNumber = int.Parse(this.clientPath.Substring(this.clientPath.Length - 10, 10));
        this.clientId = this.clientPath.Substring(this.clientPath.LastIndexOf("/", StringComparison.Ordinal) + 1);
    }

    private async Task<bool> CacheEpochLocallyAsync()
    {
        try
        {
            this.epoch = await this.zooKeeperService.GetEpochAsync();
            return true;
        }
        catch (ZkInvalidOperationException e)
        {
            this.logger.Error(this.clientId, "Could not cache the epoch. ", e);
            return false;
        }
    }

    private async Task<(ElectionResult, string)> DetermineLeadershipAsync()
    {
        this.logger.Info(this.clientId, "Looking for a next smaller sibling to watch");
        (var success, var lowerSiblingPath) = await this.FindLowerSiblingAsync();
        if (success)
        {
            if (lowerSiblingPath == string.Empty)
            {
                return (ElectionResult.IsLeader, string.Empty);
            }

            return (ElectionResult.IsFollower, lowerSiblingPath);
        }

        return (ElectionResult.Error, string.Empty);
    }

    private async Task<(bool, string)> FindLowerSiblingAsync()
    {
        try
        {
            var maxClientNumber = -1;
            var watchChild = string.Empty;
            var clients = await this.zooKeeperService.GetActiveClientsAsync();
            foreach (var childPath in clients.ClientPaths)
            {
                var siblingClientNumber = int.Parse(childPath.Substring(childPath.Length - 10, 10));
                if (siblingClientNumber > maxClientNumber && siblingClientNumber < this.clientNumber)
                {
                    watchChild = childPath;
                    maxClientNumber = siblingClientNumber;
                }
            }

            if (maxClientNumber == -1)
            {
                return (true, string.Empty);
            }

            return (true, watchChild);
        }
        catch (ZkInvalidOperationException e)
        {
            this.logger.Error(this.clientId, "Unable to determine if there are sibling clients with a lower id", e);
            return (false, string.Empty);
        }
    }

    private async Task<CoordinatorExitReason> BecomeCoordinatorAsync(CancellationToken token)
    {
        this.logger.Info(this.clientId, "Becoming coordinator");
        ICoordinator coordinator;
        switch (this.rebalancingMode)
        {
            case RebalancingMode.GlobalBarrier:
                coordinator = new GB.Coordinator(this.zooKeeperService, this.logger, this.resourceManager,
                    this.clientId, this.minimumRebalancingInterval,
                    TimeSpan.FromMilliseconds((int)this.sessionTimeout.TotalMilliseconds / 3), this.onStartDelay,
                    token);
                break;
            case RebalancingMode.ResourceBarrier:
                coordinator = new RB.Coordinator(this.zooKeeperService, this.logger, this.resourceManager,
                    this.clientId, this.minimumRebalancingInterval,
                    TimeSpan.FromMilliseconds((int)this.sessionTimeout.TotalMilliseconds / 3), this.onStartDelay,
                    token);
                break;
            default:
                coordinator = new RB.Coordinator(this.zooKeeperService, this.logger, this.resourceManager,
                    this.clientId, this.minimumRebalancingInterval,
                    TimeSpan.FromMilliseconds((int)this.sessionTimeout.TotalMilliseconds / 3), this.onStartDelay,
                    token);
                break;
        }

        var hasBecome = await coordinator.BecomeCoordinatorAsync(this.epoch);
        switch (hasBecome)
        {
            case BecomeCoordinatorResult.Ok:
                this.logger.Info(this.clientId, "Have successfully become the coordinator");

                // this blocks until coordinator terminates (due to failure, session expiry or detects it is a zombie)
                var coordinatorExitReason = await coordinator.StartEventLoopAsync();
                this.logger.Info(this.clientId, $"The coordinator has exited for reason {coordinatorExitReason}");
                return coordinatorExitReason;
            case BecomeCoordinatorResult.StaleEpoch:
                this.logger.Info(this.clientId,
                    "Since being elected, the epoch has been incremented suggesting another leader. Aborting coordinator role to check leadership again");
                return CoordinatorExitReason.NoLongerCoordinator;
            default:
                this.logger.Error(this.clientId, "Could not become coordinator");
                return CoordinatorExitReason.PotentialInconsistentState;
        }
    }

    private async Task<FollowerExitReason> BecomeFollowerAsync(CancellationToken token)
    {
        this.logger.Info(this.clientId, "Becoming a follower");

        IFollower follower;
        switch (this.rebalancingMode)
        {
            case RebalancingMode.GlobalBarrier:
                follower = new GB.Follower(this.zooKeeperService, this.logger, this.resourceManager, this.clientId,
                    this.clientNumber, this.watchSiblingNodePath,
                    TimeSpan.FromMilliseconds((int)this.sessionTimeout.TotalMilliseconds / 3), this.onStartDelay,
                    token);
                break;
            case RebalancingMode.ResourceBarrier:
                follower = new RB.Follower(this.zooKeeperService, this.logger, this.resourceManager, this.clientId,
                    this.clientNumber, this.watchSiblingNodePath,
                    TimeSpan.FromMilliseconds((int)this.sessionTimeout.TotalMilliseconds / 3), this.onStartDelay,
                    token);
                break;
            default:
                follower = new RB.Follower(this.zooKeeperService, this.logger, this.resourceManager, this.clientId,
                    this.clientNumber, this.watchSiblingNodePath,
                    TimeSpan.FromMilliseconds((int)this.sessionTimeout.TotalMilliseconds / 3), this.onStartDelay,
                    token);
                break;
        }

        var hasBecome = await follower.BecomeFollowerAsync();
        switch (hasBecome)
        {
            case BecomeFollowerResult.Ok:
                this.logger.Info(this.clientId, "Have become a follower, starting follower event loop");

                // blocks until follower either fails, the session expires or the follower detects it might be the new leader
                var followerExitReason = await follower.StartEventLoopAsync();
                this.logger.Info(this.clientId, $"The follower has exited for reason {followerExitReason}");
                return followerExitReason;
            case BecomeFollowerResult.WatchSiblingGone:
                this.logger.Info(this.clientId, "The follower was unable to watch its sibling as the sibling has gone");
                return FollowerExitReason.PossibleRoleChange;
            default:
                this.logger.Error(this.clientId, "Could not become a follower");
                return FollowerExitReason.PotentialInconsistentState;
        }
    }

    private async Task TerminateAsync(string terminationReason, bool aborted, Exception abortException = null)
    {
        try
        {
            this.state = ClientInternalState.Terminated;
            if (aborted)
            {
                if (abortException != null)
                {
                    this.logger.Error(this.clientId,
                        $"Client aborting due: {terminationReason}. Exception: {abortException}");
                }
                else
                {
                    this.logger.Error(this.clientId, $"Client aborting due: {terminationReason}");
                }
            }
            else
            {
                this.logger.Info(this.clientId, $"Client terminating due: {terminationReason}");
            }

            await this.zooKeeperService.CloseSessionAsync();
            await this.resourceManager.InvokeOnStopActionsAsync(this.clientId, "No role");

            if (aborted)
            {
                await this.resourceManager.InvokeOnAbortActionsAsync(this.clientId,
                    $"The client has aborted due to: {terminationReason}", abortException);
            }

            this.logger.Info(this.clientId, "Client terminated");
        }
        catch (TerminateClientException e)
        {
            this.logger.Error(this.clientId, "Client termination failure during invocation of on stop/abort actions",
                e);
        }
        finally
        {
            if (aborted)
            {
                this.aborted = true;
            }

            this.started = false;
        }
    }

    private async Task WaitRandomTime(TimeSpan maxWait) =>
        await Task.Delay(this.rand.Next((int)maxWait.TotalMilliseconds));
}
