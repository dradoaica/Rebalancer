namespace Rebalancer.ZooKeeper.Tests.RandomisedTests.TestComponents;

using System;

public class ErrorViolation
{
    public ErrorViolation(string message) => this.Message = message;

    public ErrorViolation(string message, Exception ex)
    {
        this.Message = message;
        this.Exception = ex;
    }

    public string Message { get; set; }
    public Exception Exception { get; set; }

    public override string ToString()
    {
        if (this.Exception != null)
        {
            return this.Exception.ToString();
        }

        return this.Message;
    }
}
