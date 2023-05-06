namespace Rebalancer.Core;

using System;

public class RebalancerException : Exception
{
    public RebalancerException(string message)
        : base(message)
    {
    }

    public RebalancerException(string message, Exception ex)
        : base(message, ex)
    {
    }
}
