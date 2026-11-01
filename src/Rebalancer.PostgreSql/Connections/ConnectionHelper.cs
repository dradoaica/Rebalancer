using Npgsql;
using System;
using System.Threading.Tasks;

namespace Rebalancer.PostgreSql.Connections;

internal static class ConnectionHelper
{
    // as the SQL Server backend: up to three tries, waiting 2 s and then 4 s between them
    public static async Task<NpgsqlConnection> GetOpenConnectionAsync(string connectionString)
    {
        var tries = 0;
        while (true)
        {
            tries++;
            NpgsqlConnection connection = new(connectionString);
            try
            {
                await connection.OpenAsync();
                return connection;
            }
            catch (Exception ex) when (TransientErrorDetector.IsTransient(ex))
            {
                await connection.DisposeAsync();
                if (tries == 3)
                {
                    throw;
                }

                await Task.Delay(TimeSpan.FromSeconds(tries * 2));
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }
    }
}
