# Rebalancer — architecture overview

High-level view of the .NET solution. Libraries live under [`src/`](../../src/); samples under [`examples/`](../../examples/); automated tests under [`tests/`](../../tests/). For protocol detail, see [src/Rebalancer.ZooKeeper/README.md](../../src/Rebalancer.ZooKeeper/README.md) and [README.md](../../README.md).

```mermaid
flowchart TB
  subgraph host [Host application]
    RC[RebalancerClient]
    CB[Callbacks OnAssignment OnUnassignment OnAborted]
    RC --> CB
  end
  subgraph core [Rebalancer.Core]
    IP[IRebalancerProvider]
    OPT[ClientOptions]
    LOG[IRebalancerLogger]
  end
  RC --> IP
  RC --> OPT
  IP --> LOG
  subgraph backends [Backends]
    SS[SqlServerProvider]
    RD[RedisProvider]
    ZK[ZooKeeperProvider]
  end
  IP --> SS
  IP --> RD
  IP --> ZK
  subgraph stores [Coordination stores]
    SQL[(SQL Server)]
    RDIS[(Redis)]
    ZKN[ZooKeeper ensemble]
  end
  SS --> SQL
  RD --> RDIS
  ZK --> ZKN
```

**Registration:** static `Providers.Register(() => …)` **or** `Microsoft.Extensions.DependencyInjection` extensions in `src/Rebalancer.Extensions.DependencyInjection` ([ADR 0005](../adrs/0005-dependency-injection-for-provider-registration-v2.md)).
