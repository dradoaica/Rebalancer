# Contributing to Rebalancer

Thank you for your interest in Rebalancer! This is a .NET library for Kafka-style consumer groups over resources,
coordinated through SQL Server, PostgreSQL, Redis or ZooKeeper. Its coordination protocols aim to assign each resource
to one node at a time. Resource handlers are not fenced, however, so a long-paused node may continue processing briefly
after reassignment ([ADR-0021](docs/adrs/0021-assignment-generation-for-resource-handlers.md)). Contributions are judged
first by whether they preserve coordination safety, then by everything else.

For security issues, follow [SECURITY.md](SECURITY.md) instead of opening an issue.

## Ways to contribute

- **Bugs:** open an issue with the package and version, the backend, what you expected and what happened. Logs from
  every node of the group, with timestamps, help most – rebalancing bugs are usually about ordering between nodes.
- **Typos and small doc fixes:** open a pull request directly; no issue needed.
- **Features and design changes:** open an issue first to discuss. Read the
  [product baseline](docs/initial-product-specs/rebalancer-product-baseline.md) for scope and non-goals, and the
  [architecture overview](docs/architecture/rebalancer-overview.md) for how the parts fit.
- **New backends** (etcd, Consul, …): welcome, as a separate package implementing `IRebalancerProvider`.
  Expect to bring a threat model and the cross-backend safety tests along with it (see below).

## Development setup

You need:

- the .NET SDK pinned in [`global.json`](global.json) (10.0, rolls forward). Library packages target `net10.0`
  ([ADR-0033](docs/adrs/0033-target-net-10-only.md));
- Docker, for the SQL Server, PostgreSQL, Redis and Valkey integration tests (Testcontainers starts the servers; the
  tests are skipped when Docker is not available);
- for the ZooKeeper integration tests, a three-node ZooKeeper ensemble on `localhost:2181`, `2182` and `2183`. These
  tests are **not** skipped automatically – they fail without the ensemble. `DisconnectionTests` and
  `NetworkFaultTests` pause or cut off the ensemble's containers and need them under the names the [README](README.md)
  uses (`rbr-it-zk1`..`3`); they are skipped otherwise.

```shell
dotnet build Rebalancer.slnx
dotnet test tests/Rebalancer.UnitTests
# SQL Server, PostgreSQL and Redis (Docker); with less than 8 GiB for Docker, add: -- xUnit.MaxParallelThreads=3
dotnet test tests/Rebalancer.IntegrationTests --filter "FullyQualifiedName!~ZooKeeper"
```

The ZooKeeper suites also run the randomised tests, about 46 hours in total; cap each with
`REBALANCER_RANDOMISED_TEST_MAX_MINUTES` (see the README, "Building and testing").

Repository layout:

| Path        | Contents                                                                                                                        |
|-------------|---------------------------------------------------------------------------------------------------------------------------------|
| `src/`      | Published libraries: `Rebalancer.Core`, `Rebalancer.LeaseCoordination`, four backend packages, and DI / health-check extensions |
| `tests/`    | Unit tests (no external services) and integration tests (real backends)                                                         |
| `db/`       | Schema, tables and least-privilege role per database: `sqlserver/`, `postgresql/`                                               |
| `examples/` | RabbitMQ samples (quorum queues, super streams) and a queue-setup CLI; not published                                            |
| `docs/`     | Decision records, user stories, threat models, change/problem records, release notes, guides                                    |

Follow the formatting in [`.editorconfig`](.editorconfig).

## How changes are governed

Rebalancer is developed spec-first: every non-trivial change traces back to a document in `docs/`. The full contract is
in [`.agents/AGENTS.md`](.agents/AGENTS.md) (written for AI agents, but it applies to humans too); the short version:

| Tier | What                                                                                   | What you need                                                           |
|------|----------------------------------------------------------------------------------------|-------------------------------------------------------------------------|
| 1    | New dependency, public API or config change, store schema/key change, security control | An **accepted** ADR in [`docs/adrs/`](docs/adrs/) before implementation |
| 2    | New capability within existing ADRs                                                    | A `Ready` user story in [`docs/user-stories/`](docs/user-stories/)      |
| 3    | Bug fix, refactor, test improvement                                                    | A reference to the issue or story, and a regression test                |
| 4    | Typos, comments, formatting                                                            | Nothing beyond the PR                                                   |

If you are unsure of the tier, pick the higher one. For Tier 1 and 2, you are welcome to draft the ADR or story yourself
from the templates in those folders and propose it in its own pull request; a maintainer approves it. Accepted ADRs are
not edited – a changed decision gets a new, superseding ADR.

Things that are Tier 1 here even when they look small:

- changing what is stored in SQL Server or PostgreSQL tables, Redis keys or znodes – nodes of different versions share
  the store during a rolling upgrade;
- changing timing (lease, keep-alive, expiry) or the order of steps in a rebalance;
- anything in the public surface of `RebalancerClient`, `IRebalancerProvider`, options or DI registrations;
- adding or upgrading a NuGet dependency of a package in `src/`.

## Pull requests

- State the **tier** and the issue, story or ADR the change implements in the description.
- Keep `dotnet build` free of new warnings and all tests green. Say which integration tests you ran and against which
  backends; if you could not run one, say so.
- **Tests:** a change to coordination must come with a test that would fail without it. Prefer the cross-backend
  scenarios (paused nodes, randomised schedules) in `tests/Rebalancer.IntegrationTests/` over single-backend tests –
  see [US-0013](docs/user-stories/US-0013-cross-backend-assignment-safety-tests.md).
- **Security posture:** if the change touches credentials, permissions, the store layout or what leaves the process
  (logs, metrics, traces, health output), update the threat model of each affected backend in
  [`docs/threat-models/`](docs/threat-models/).
- **Docs:** update the [traceability matrix](docs/traceability-matrix.md) when you add an ADR, story or significant
  path, and describe user-visible changes for the next release notes in the PR.
- Keep each pull request to one concern; a governance document and its implementation go in separate pull requests.

## Design principles

- **Safety over availability.** When in doubt, a node stops working on a resource. A brief gap in processing is
  acceptable; two consumers on one ordered queue is not.
- **Backend-agnostic core.** `Rebalancer.Core` owns the common client API and lifecycle. SQL Server, PostgreSQL and
  Redis
  share their lease/coordinator/follower implementation in `Rebalancer.LeaseCoordination`, while each provider supplies
  its store-specific services; ZooKeeper implements its own election and barrier protocols.
- **Small dependency footprint.** Each package pulls only what its backend needs. New dependencies need a reason that
  outweighs the cost to every user.
- **Compatibility is deliberate.** Breaking changes go into a major version, with deprecations in a prior minor release
  and a migration guide in [`docs/release-notes/`](docs/release-notes/).
- **Performance claims need numbers.** Include before/after measurements; changes that make coordination code harder to
  reason about get more scrutiny.

## AI-assisted contributions

AI-assisted contributions are welcome under the same rules. Agents in this repository follow
[`.agents/AGENTS.md`](.agents/AGENTS.md) and may draft ADRs, stories and code, but never approve or merge their own work
([rule 00](.agents/rules/00-no-self-approval.md)). You remain responsible for every line you submit.

## Code of Conduct

Please follow our [Code of Conduct](CODE_OF_CONDUCT.md). Respectful collaboration is key to a successful project.
