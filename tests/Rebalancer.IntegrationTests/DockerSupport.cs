using Testcontainers.Redis;

namespace Rebalancer.IntegrationTests;

internal static class DockerSupport
{
    /// <summary>True if Testcontainers can reach Docker (cheap check via Redis builder validation).</summary>
    public static bool IsDockerAvailable()
    {
        try
        {
            _ = new RedisBuilder().Build();
            return true;
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
    }
}
