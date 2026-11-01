using Testcontainers.Redis;
using Xunit;

namespace Rebalancer.IntegrationTests;

internal static class DockerSupport
{
    // pinned to the images Testcontainers 4.16 used by default, so test behavior does not change silently
    public const string RedisImage = "redis:7.0";
    public const string MsSqlImage = "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04";

    // the Valkey version Rebalancer.Redis is supported on (ADR-0031, US-0025); a newer one only after the suites pass
    public const string ValkeyImage = "valkey/valkey:8.1.11";

    // the PostgreSQL version the PostgreSQL backend is tested on (ADR-0030, US-0026)
    public const string PostgreSqlImage = "postgres:17.8";

    /// <summary>The servers every Redis integration test runs on (US-0025): Redis and Valkey.</summary>
    public static TheoryData<string> RedisServers => new()
    {
        RedisImage,
        ValkeyImage,
    };

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
