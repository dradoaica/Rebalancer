﻿namespace Rebalancer.SqlServer.Connections;

using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

public static class ConnectionHelper
{
    public static async Task<SqlConnection> GetOpenConnectionAsync(string connectionString)
    {
        var tries = 0;
        while (tries <= 3)
        {
            tries++;
            try
            {
                SqlConnection connection = new(connectionString);
                await connection.OpenAsync();
                return connection;
            }
            catch (Exception ex) when (TransientErrorDetector.IsTransient(ex))
            {
                if (tries == 3)
                {
                    throw;
                }

                // wait 1, 2, 4 seconds -- would be nice to not to delay cancellation here
                await Task.Delay(TimeSpan.FromSeconds(tries * 2));
            }
        }

        return null;
    }
}