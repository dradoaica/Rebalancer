using Testcontainers.Redis;

namespace Rebalancer.IntegrationTests;

internal static class DockerSupport
{
    // pinned to the images Testcontainers 4.16 used by default, so test behavior does not change silently
    public const string RedisImage = "redis:7.0";
    public const string MsSqlImage = "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04";

    /// <summary>True if Testcontainers can reach Docker (cheap check via Redis builder validation).</summary>
    public static bool IsDockerAvailable()
    {
        try
        {
            _ = new RedisBuilder(RedisImage).Build();
            return true;
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
    }
}
