using Rebalancer.Core;

namespace Rebalancer.IntegrationTests;

/// <summary>
///     Stands in for the system the resource handlers work on. A node processes a resource from the
///     <see cref="RebalancerClient.OnAssignment" /> that gives it the resource until the
///     <see cref="RebalancerClient.OnUnassignment" /> that takes it back returns. Every moment two nodes process the same
///     resource is recorded as an overlap: the downstream system would see that resource processed twice at once.
/// </summary>
/// <remarks>
///     US-0024: a paused node (<see cref="Pause" />) processes nothing until it resumes, as a stalled process would;
///     its handler events meanwhile take effect at the resume. A resumed node may overlap on the resources it processed
///     before the pause for a window (ADR-0019, accepted by ADR-0021): an overlap it ends with OnUnassignment within the
///     window is not recorded; one that outlasts it is, at the next <see cref="CheckWindows" />.
/// </remarks>
internal sealed class ProcessingMonitor
{
    private readonly HashSet<string> crashed = [];
    private readonly Func<DateTime> now;
    private readonly List<string> overlaps = [];
    private readonly Dictionary<string, HashSet<string>> processing = new();
    private readonly Dictionary<string, HashSet<string>> suspended = new();
    private readonly object sync = new();
    private readonly List<ResumeWindow> windows = [];

    public ProcessingMonitor()
        : this(() => DateTime.UtcNow)
    {
    }

    public ProcessingMonitor(Func<DateTime> now)
    {
        this.now = now;
    }

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
            suspended.Remove(node);
            StopLocked(node);
        }
    }

    /// <summary>The node's process stalls: it processes nothing until <see cref="Resume" />.</summary>
    public void Pause(string node)
    {
        lock (sync)
        {
            if (crashed.Contains(node) || suspended.ContainsKey(node))
            {
                return;
            }

            suspended[node] = processing.Where(x => x.Value.Contains(node)).Select(x => x.Key).ToHashSet();
            StopLocked(node);
        }
    }

    /// <summary>
    ///     The node's process runs again, on what it processed before <see cref="Pause" /> (or what its handler events
    ///     since then left it). Each overlap this creates must end within <paramref name="window" />.
    /// </summary>
    public void Resume(string node, TimeSpan window)
    {
        lock (sync)
        {
            if (!suspended.Remove(node, out var held))
            {
                return;
            }

            // a window on every resource the node resumes with, also when no other node processes it yet: the node it was
            // reassigned to may start it only after the resume, before this node's first tick (ADR-0019, ADR-0021)
            var deadline = now() + window;
            foreach (var resource in held)
            {
                windows.Add(new ResumeWindow(node, resource, deadline, window));
                ProcessorsOf(resource).Add(node);
            }
        }
    }

    /// <summary>Records every resume window that ended with its overlap still there. Call it regularly.</summary>
    public void CheckWindows()
    {
        lock (sync)
        {
            var time = now();
            foreach (var window in windows.Where(x => time > x.Deadline).ToList())
            {
                windows.Remove(window);
                if (processing.TryGetValue(window.Resource, out var by) && by.Contains(window.Node) && by.Count > 1)
                {
                    overlaps.Add(
                        $"{time:HH:mm:ss.fff} {window.Resource}: {window.Node} still processed it " +
                        $"{window.Length.TotalSeconds} s after resuming, while {string.Join(",", by.Where(x => x != window.Node))} did"
                    );
                }
            }
        }
    }

    /// <summary>Whether any node processes <paramref name="resource" /> now.</summary>
    public bool IsProcessed(string resource)
    {
        lock (sync)
        {
            return processing.TryGetValue(resource, out var by) && by.Any();
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

            if (suspended.ContainsKey(node))
            {
                suspended[node] = resources.ToHashSet(); // takes effect at the resume
                return;
            }

            StopLocked(node);
            foreach (var resource in resources)
            {
                var by = ProcessorsOf(resource);

                // a resumed node inside its window on this resource is already accounted for by that window
                var others = by.Where(x => !windows.Any(w => w.Node == x && w.Resource == resource)).ToList();
                if (others.Any())
                {
                    overlaps.Add(
                        $"{now():HH:mm:ss.fff} {resource}: {node} started while {string.Join(",", others)} processed it"
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
            if (suspended.TryGetValue(node, out var held))
            {
                held.Clear(); // takes effect at the resume
                return;
            }

            StopLocked(node);
        }
    }

    private void StopLocked(string node)
    {
        foreach (var by in processing.Values)
        {
            by.Remove(node);
        }

        windows.RemoveAll(x => x.Node == node);
    }

    private HashSet<string> ProcessorsOf(string resource)
    {
        if (!processing.TryGetValue(resource, out var by))
        {
            by = [];
            processing[resource] = by;
        }

        return by;
    }

    private sealed record ResumeWindow(string Node, string Resource, DateTime Deadline, TimeSpan Length);
}
