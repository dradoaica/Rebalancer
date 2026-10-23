using Rebalancer.Core;

namespace Rebalancer.IntegrationTests;

/// <summary>
///     Stands in for the system the resource handlers work on. A node processes a resource from the
///     <see cref="RebalancerClient.OnAssignment" /> that gives it the resource until the
///     <see cref="RebalancerClient.OnUnassignment" /> that takes it back returns. Every moment two nodes process the same
///     resource is recorded as an overlap: the downstream system would see that resource processed twice at once.
/// </summary>
internal sealed class ProcessingMonitor
{
    private readonly HashSet<string> crashed = [];
    private readonly List<string> overlaps = [];
    private readonly Dictionary<string, HashSet<string>> processing = new();
    private readonly object sync = new();

    public IReadOnlyList<string> Overlaps
    {
        get
        {
            lock (sync)
            {
                return overlaps.ToList();
            }
        }
    }

    /// <summary>Processes what <paramref name="client" /> is assigned, as node <paramref name="node" />.</summary>
    public void Attach(RebalancerClient client, string node)
    {
        client.OnAssignment += (_, args) => Start(node, args.Resources);
        client.OnUnassignment += (_, _) => Stop(node);
    }

    /// <summary>The node's process dies: its processing ends, and whatever its client still reports is ignored.</summary>
    public void Crash(string node)
    {
        lock (sync)
        {
            crashed.Add(node);
            StopLocked(node);
        }
    }

    /// <summary>The resources <paramref name="node" /> processes now.</summary>
    public IReadOnlyList<string> ProcessedBy(string node)
    {
        lock (sync)
        {
            return processing.Where(x => x.Value.Contains(node)).Select(x => x.Key).OrderBy(x => x).ToList();
        }
    }

    /// <summary>
    ///     True when each of <paramref name="resources" /> is processed by exactly one node, all of them among
    ///     <paramref name="nodes" />, and each of those nodes processes at least one resource.
    /// </summary>
    public bool Converged(IReadOnlyCollection<string> resources, IReadOnlyCollection<string> nodes)
    {
        lock (sync)
        {
            return resources.All(r =>
                       processing.TryGetValue(r, out var by) && by.Count == 1 && nodes.Contains(by.First())
                   ) &&
                   nodes.All(n => processing.Values.Any(by => by.Contains(n)));
        }
    }

    private void Start(string node, IEnumerable<string> resources)
    {
        lock (sync)
        {
            if (crashed.Contains(node))
            {
                return;
            }

            StopLocked(node);
            foreach (var resource in resources)
            {
                if (!processing.TryGetValue(resource, out var by))
                {
                    by = [];
                    processing[resource] = by;
                }

                if (by.Any())
                {
                    overlaps.Add(
                        $"{DateTime.UtcNow:HH:mm:ss.fff} {resource}: {node} started while {string.Join(",", by)} processed it"
                    );
                }

                by.Add(node);
            }
        }
    }

    private void Stop(string node)
    {
        lock (sync)
        {
            StopLocked(node);
        }
    }

    private void StopLocked(string node)
    {
        foreach (var by in processing.Values)
        {
            by.Remove(node);
        }
    }
}
