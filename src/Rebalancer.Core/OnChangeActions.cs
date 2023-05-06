namespace Rebalancer.Core;

using System;
using System.Collections.Generic;

/// <summary>
///     A wrapper class for all start, stop and abort actions.
///     This is only of note for provider library implementers.
/// </summary>
public sealed class OnChangeActions
{
    public OnChangeActions()
    {
        this.OnStartActions = new List<Action<IList<string>>>();
        this.OnStopActions = new List<Action>();
        this.OnAbortActions = new List<Action<string, Exception>>();
    }

    public IList<Action<IList<string>>> OnStartActions { get; set; }
    public IList<Action> OnStopActions { get; set; }
    public IList<Action<string, Exception>> OnAbortActions { get; set; }

    public void AddOnStartAction(Action<IList<string>> action) => this.OnStartActions.Add(action);

    public void AddOnStopAction(Action action) => this.OnStopActions.Add(action);

    public void AddOnAbortAction(Action<string, Exception> action) => this.OnAbortActions.Add(action);
}
