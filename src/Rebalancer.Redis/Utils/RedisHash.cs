using Newtonsoft.Json;
using Rebalancer.Core;
using StackExchange.Redis;
using System;
using System.Threading.Tasks;

namespace Rebalancer.Redis.Utils;

/// <summary>
///     Atomic hash access using the same key/field/value encoding as StackExchange.Redis.DataTypes RedisDictionary
///     (and the same key encoding as its RedisList), so data written by earlier versions stays readable.
/// </summary>
internal static class RedisHash
{
    private const int MaxCompareAndSetAttempts = 10;

    public static RedisKey DictionaryKey(string name) => $"Dictionary:{Constants.SCHEMA}:{name}";

    public static RedisKey ListKey(string name) => $"List:{Constants.SCHEMA}:{name}";

    public static RedisValue Encode(object value) => JsonConvert.SerializeObject(value);

    public static T Decode<T>(RedisValue value) => JsonConvert.DeserializeObject<T>(value);

    /// <summary>Redis server time, so lease and keep alive expiry do not depend on client clocks.</summary>
    public static async Task<DateTime> GetServerTimeAsync(IDatabase db)
    {
        var time = (RedisResult[])await db.ExecuteAsync("TIME");
        return DateTimeOffset.FromUnixTimeSeconds((long)time[0]).UtcDateTime.AddTicks((long)time[1] * 10);
    }

    /// <summary>
    ///     Optimistic compare-and-set of one hash field. <paramref name="decide" /> gets the current value (null when
    ///     missing) and the server time, and returns the value to write (null for no write) plus the result. If another writer
    ///     changed the field in the meantime, nothing is written and the decision is re-evaluated.
    /// </summary>
    public static async Task<TResult> CompareAndSetAsync<T, TResult>(
        IDatabase db,
        RedisKey key,
        RedisValue field,
        Func<T, DateTime, (T NewValue, TResult Result)> decide
    )
        where T : class
    {
        for (var attempt = 0; attempt < MaxCompareAndSetAttempts; attempt++)
        {
            var current = await db.HashGetAsync(key, field);
            var now = await GetServerTimeAsync(db);
            var (newValue, result) = decide(current.IsNull ? null : Decode<T>(current), now);
            if (newValue == null)
            {
                return result;
            }

            var transaction = db.CreateTransaction();
            transaction.AddCondition(
                current.IsNull ? Condition.HashNotExists(key, field) : Condition.HashEqual(key, field, current)
            );
            _ = transaction.HashSetAsync(key, field, Encode(newValue));
            if (await transaction.ExecuteAsync())
            {
                return result;
            }
        }

        throw new RebalancerException($"Could not update {key} {field} due to concurrent modifications");
    }
}
