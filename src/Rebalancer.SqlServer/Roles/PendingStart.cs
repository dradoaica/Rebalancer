using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Rebalancer.SqlServer.Roles;

/// <summary>
///     PRB-0007: a grant this node has seen but not started yet, because <c>ClientOptions.OnAssignmentDelay</c> has
///     not elapsed since it saw it.
/// </summary>
internal class PendingStart
{
    private readonly Stopwatch since = Stopwatch.StartNew();

    public PendingStart(List<string> resources, int fencingToken)
    {
        Resources = resources.ToList();
        FencingToken = fencingToken;
    }

    public List<string> Resources { get; }
    public int FencingToken { get; }
    public TimeSpan Elapsed => since.Elapsed;

    /// <summary>Whether a grant read now is the same grant: the same resources under the same fencing token.</summary>
    public bool Matches(List<string> resources, int fencingToken) =>
        fencingToken == FencingToken && resources.OrderBy(x => x).SequenceEqual(Resources.OrderBy(x => x));
}
