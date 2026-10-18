using Microsoft.Data.SqlClient;
using System;
using System.Threading.Tasks;

namespace Rebalancer.SqlServer.Connections;

public static class ConnectionHelper
{
    public static async Task<SqlConnection> GetOpenConnectionAsync(string connectionString)
    {
        var tries = 0;
        while (true)
        {
            tries++;
            SqlConnection connection = new(connectionString);
            try
            {
                await connection.OpenAsync();
                return connection;
            }
            catch (Exception ex) when (TransientErrorDetector.IsTransient(ex))
            {
                connection.Dispose();
                if (tries == 3)
                {
                    throw;
                }

                // wait 2, 4 seconds -- would be nice to not to delay cancellation here
                await Task.Delay(TimeSpan.FromSeconds(tries * 2));
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }
    }
}
