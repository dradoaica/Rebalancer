namespace Rebalancer.IntegrationTests.ZooKeeper.RandomisedTests.TestComponents;

public class ErrorViolation(string message, Exception ex = null)
{
    public string Message { get; } = message;
    public Exception Exception { get; } = ex;

    public override string ToString()
    {
        if (Exception != null)
        {
            return Exception.ToString();
        }

        return Message;
    }
}
