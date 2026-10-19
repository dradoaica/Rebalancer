using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.Redis.Resources;
using StackExchange.Redis;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Rebalancer.Redis.Utils;

/// <summary>
///     ADR-0020: 3.x kept every group's settings / lease and resources in two shared keys; 4.0 keeps them per group. The
///     first 4.0 node of a group moves its entries in one guarded transaction, so at most one node moves them.
/// </summary>
internal static class PerGroupKeyMigration
{
    private const int MaxAttempts = 10;
    private static readonly RedisKey SharedResourceGroupsKey = RedisHash.DictionaryKey("ResourceGroups");
    private static readonly RedisKey SharedResourcesKey = RedisHash.ListKey("Resources");

    public static async Task MoveToPerGroupKeysAsync(IDatabase db, string resourceGroup, IRebalancerLogger logger, string clientId)
    {
        var leaseKey = RedisHash.LeaseKey(resourceGroup);
        var resourcesKey = RedisHash.ResourcesKey(resourceGroup);
        var field = RedisHash.Encode(resourceGroup);
        try
        {
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                // moved already (or provisioned in 4.0): never touch the shared keys, so a per-group ACL user works
                if (await db.KeyExistsAsync(leaseKey))
                {
                    return;
                }

                var lease = await db.HashGetAsync(SharedResourceGroupsKey, field);
                if (lease.IsNull)
                {
                    return; // not provisioned in either layout: the lease service reports that the group does not exist
                }

                var shared = await db.ListRangeAsync(SharedResourcesKey);
                var own = shared.Where(x => RedisHash.Decode<Resource>(x).ResourceGroup == resourceGroup).ToArray();

                var transaction = db.CreateTransaction();
                transaction.AddCondition(Condition.KeyNotExists(leaseKey));
                transaction.AddCondition(Condition.HashEqual(SharedResourceGroupsKey, field, lease));
                transaction.AddCondition(Condition.ListLengthEqual(SharedResourcesKey, shared.Length));
                _ = transaction.HashSetAsync(leaseKey, field, lease);
                _ = transaction.HashDeleteAsync(SharedResourceGroupsKey, field);
                if (own.Any())
                {
                    // the shared list is the group's truth while its lease key is missing; a per-group list left over
                    // from an earlier 4.x run (e.g. after a rollback to 3.x) is replaced, not appended to
                    _ = transaction.KeyDeleteAsync(resourcesKey);
                    _ = transaction.ListRightPushAsync(resourcesKey, own);
                    foreach (var value in own.Distinct())
                    {
                        _ = transaction.ListRemoveAsync(SharedResourcesKey, value);
                    }
                }

                if (await transaction.ExecuteAsync())
                {
                    logger.Info(
                        clientId,
                        $"Moved resource group '{resourceGroup}' and {own.Length} resource(s) to per-group keys (ADR-0020)"
                    );
                    return;
                }
            }

            throw new RebalancerException(
                $"Could not move resource group '{resourceGroup}' to per-group keys: the shared keys kept changing."
            );
        }
        catch (RedisServerException ex) when (ex.Message.StartsWith("NOPERM", StringComparison.Ordinal))
        {
            throw new RebalancerException(
                $"Resource group '{resourceGroup}' is not in {leaseKey} yet, and this Redis user cannot read the shared " +
                "keys to move it there. Start once with a user that can, or provision the per-group keys " +
                "(docs/release-notes/migrating-to-4.0.md).",
                ex
            );
        }
    }
}
