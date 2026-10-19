# Migrating from 3.x to 4.0 (Redis)

Only Redis deployments are affected. SQL Server and ZooKeeper have no 4.0 migration steps.

Decision: [ADR-0020](../adrs/0020-per-group-redis-leases-and-resources.md). Since 3.2.0, every Redis client logs a
warning once per session that points here (US-0016).

## What changes

4.0 moves each resource group's lease and resources out of the keys that all groups share, as 3.0 did for client
records (ADR-0015). In the key names, `{group}` is the group name percent-encoded outside `A-Z a-z 0-9 - _ . ~`, e.g.
`orders` stays `orders` and `eu orders` becomes `eu%20orders`.

| Data                                | 3.x (shared by all groups)                              | 4.0 (one key per group)                                               |
|-------------------------------------|---------------------------------------------------------|-----------------------------------------------------------------------|
| Group settings and lease            | field `"{name}"` of hash `Dictionary:RBR:ResourceGroups` | hash `RBR:Lease:{group}`, same single field and value                  |
| Resources                           | entries of list `List:RBR:Resources`                    | list `RBR:Resources:{group}`, same entry values                       |
| Client records                      | `RBR:Clients:{group}` (since 3.0)                       | unchanged                                                             |

Values keep their JSON format. The field name in `RBR:Lease:{group}` is the JSON-encoded group name (with quotes), as in
the shared hash.

## Upgrading: automatic, once per group

1. Stop every node of the group. A group must not run 3.x and 4.0 nodes together: they would read different keys
   (the all-nodes-at-once rule that applies to Redis since 2.1).
2. Deploy 4.0 and start the nodes. When a node starts and `RBR:Lease:{group}` does not exist, it moves the group's
   field from `Dictionary:RBR:ResourceGroups` and the group's entries from `List:RBR:Resources` to the new keys in one
   Redis transaction. The transaction fails if any of them changed meanwhile, so at most one node moves them; the
   others find the new keys and skip the step.
3. The node that moves the group needs access to the shared keys once. Run the first 4.0 start with a user that has
   it (for example the 3.x per-group user below), or provision the new keys yourself first (next section). Afterwards
   the group needs no access to the shared keys.

A group that exists in neither layout fails to start, as in 3.x ("The resource group … does not exist").

## Provisioning a new group in 4.0

Write the group's settings and resources directly into its own keys, e.g. for `orders` with two resources:

```
HSET RBR:Lease:orders "\"orders\"" "{\"Name\":\"orders\",\"LeaseExpirySeconds\":60,\"HeartbeatSeconds\":25,\"FencingToken\":0}"
RPUSH RBR:Resources:orders "{\"ResourceGroup\":\"orders\",\"ResourceName\":\"r1\"}" "{\"ResourceGroup\":\"orders\",\"ResourceName\":\"r2\"}"
```

Update provisioning scripts that write `Dictionary:RBR:ResourceGroups` or `List:RBR:Resources`: a 4.0 node ignores
entries added there once its group has moved.

## Redis ACL user for one group

With 4.0 a user can be confined to its group completely, with no access to any shared key:

```
ACL SETUSER orders-user on >secret resetchannels ~RBR:Lease:orders ~RBR:Resources:orders ~RBR:Clients:orders +@read +@write +@transaction +time +echo
```

The command set is the one verified for 3.0 (see [migrating-to-3.0.md](migrating-to-3.0.md#redis-upgrade-all-nodes-of-a-resource-group-at-once-adr-0015)).
For 3.x, keep `~Dictionary:RBR:ResourceGroups ~List:RBR:Resources` in the user until the group has moved.

## Rollback to 3.x

3.x reads only the shared keys. Stop the group's nodes, then either move the group back (copy the field of
`RBR:Lease:{group}` into `Dictionary:RBR:ResourceGroups` and the entries of `RBR:Resources:{group}` into
`List:RBR:Resources`) or provision the group in the shared keys again, and start 3.x. Delete `RBR:Lease:{group}`
before a later 4.0 start, or that start will not move the group again and will use the old per-group copy.
