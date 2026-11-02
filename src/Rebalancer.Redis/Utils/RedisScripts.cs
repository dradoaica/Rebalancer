using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StackExchange.Redis;
using System;
using System.Threading.Tasks;

namespace Rebalancer.Redis.Utils;

/// <summary>
///     ADR-0037: the conditional writes as server-side scripts. A script checks its condition and writes in one
///     atomic step, so a write to another field or key cannot abort it (unlike <c>WATCH</c>, which covers a whole key,
///     PRB-0009). The scripts never re-encode a record (Lua's cjson turns an empty list into <c>{}</c>, which Newtonsoft
///     cannot read): they decode it to check values and write text Newtonsoft produced, filling in the fields other
///     writers own as plain text. StackExchange.Redis sends <c>EVAL</c> the first time and <c>EVALSHA</c> afterwards; its
///     <c>SCRIPT LOAD</c> is denied to the per-group ACL user and not needed, as <c>EVAL</c> already caches the script on
///     the server.
/// </summary>
internal static class RedisScripts
{
    // placeholders the .NET side writes for the fields another writer owns; the script puts the stored value there
    private const string KeepAlivePlaceholder = "RBR-KEEP-ALIVE-OF-THE-STORED-RECORD";
    private const int StatusPlaceholder = -1_037_001;
    private const int TokenPlaceholder = -1_037_002;

    // returns 1 if written, 0 if the field no longer holds the expected value (ARGV[2] = "1": expected missing)
    private const string CompareAndSetFieldScript = @"
local current = redis.call('HGET', KEYS[1], ARGV[1])
if ARGV[2] == '1' then
  if current then return 0 end
elseif current ~= ARGV[3] then
  return 0
end
redis.call('HSET', KEYS[1], ARGV[1], ARGV[4])
return 1";

    // the shared Lua helpers: plain-text replacement, and the ADR-0026 lease guard (holder and token, not the whole value)
    private const string Helpers = @"
local function replace(text, old, new)
  local i, j = string.find(text, old, 1, true)
  if not i then return nil end
  return string.sub(text, 1, i - 1) .. new .. string.sub(text, j + 1)
end
local function holds_lease(lease_key, group_field, coordinator, token)
  local lease = redis.call('HGET', lease_key, group_field)
  if not lease then return false end
  local group = cjson.decode(lease)
  return group['CoordinatorId'] == coordinator and group['FencingToken'] == token
end";

    // KEYS[1] clients hash; ARGV[1] field, ARGV[2] the new LastKeepAlive as JSON text. Returns the record, or nil
    private const string KeepAliveScript = Helpers +
                                           @"
local record = redis.call('HGET', KEYS[1], ARGV[1])
if not record then return false end
local old = cjson.decode(record)['LastKeepAlive']
local updated = replace(record, '""LastKeepAlive"":' .. cjson.encode(old), '""LastKeepAlive"":' .. ARGV[2])
if not updated then return redis.error_reply('RBR: unexpected client record format') end
redis.call('HSET', KEYS[1], ARGV[1], updated)
return updated";

    // KEYS[1] clients hash; ARGV[1] field, ARGV[2] the new ClientStatus. Returns 1, or 0 if the record is missing
    private const string SetStatusScript = Helpers +
                                           @"
local record = redis.call('HGET', KEYS[1], ARGV[1])
if not record then return 0 end
local old = cjson.decode(record)['ClientStatus']
local updated = replace(record, '""ClientStatus"":' .. tostring(old), '""ClientStatus"":' .. ARGV[2])
if not updated then return redis.error_reply('RBR: unexpected client record format') end
redis.call('HSET', KEYS[1], ARGV[1], updated)
return 1";

    // KEYS[1] clients hash, KEYS[2] lease hash; ARGV[1] group field, ARGV[2] coordinator id, ARGV[3] fencing token,
    // then field / expected value pairs. Deletes the entries still holding their value while the lease is held;
    // returns the number deleted
    private const string DeleteExpiredScript = Helpers +
                                               @"
if not holds_lease(KEYS[2], ARGV[1], ARGV[2], tonumber(ARGV[3])) then return 0 end
local deleted = 0
for i = 4, #ARGV, 2 do
  if redis.call('HGET', KEYS[1], ARGV[i]) == ARGV[i + 1] then
    deleted = deleted + redis.call('HDEL', KEYS[1], ARGV[i])
  end
end
return deleted";

    // KEYS[1] clients hash, KEYS[2] lease hash (not read if ARGV[2] is empty); ARGV[1] field, ARGV[2] lease field,
    // ARGV[3] coordinator id, ARGV[4] fencing token, ARGV[5] the new record, its placeholders replaced by the stored
    // values. Returns 1 written, 0 fenced out or record missing
    private static readonly string GuardedRecordScript = Helpers +
                                                         @"
local token = tonumber(ARGV[4])
if ARGV[2] ~= '' and not holds_lease(KEYS[2], ARGV[2], ARGV[3], token) then return 0 end
local record = redis.call('HGET', KEYS[1], ARGV[1])
if not record then return 0 end
local stored = cjson.decode(record)
if stored['FencingToken'] > token then return 0 end
local updated = ARGV[5]
updated = replace(updated, '""LastKeepAlive"":""" +
                                                         KeepAlivePlaceholder +
                                                         @"""', '""LastKeepAlive"":' .. cjson.encode(stored['LastKeepAlive'])) or updated
updated = replace(updated, '""ClientStatus"":" +
                                                         StatusPlaceholder +
                                                         @"', '""ClientStatus"":' .. tostring(stored['ClientStatus'])) or updated
updated = replace(updated, '""FencingToken"":" +
                                                         TokenPlaceholder +
                                                         @"', '""FencingToken"":' .. tostring(stored['FencingToken'])) or updated
redis.call('HSET', KEYS[1], ARGV[1], updated)
return 1";

    /// <summary>Writes <paramref name="newValue" /> if the field still holds <paramref name="expected" /> (null: missing).</summary>
    public static async Task<bool> CompareAndSetFieldAsync(
        IDatabase db,
        RedisKey key,
        RedisValue field,
        RedisValue expected,
        RedisValue newValue
    ) =>
        (int)await db.ScriptEvaluateAsync(
            CompareAndSetFieldScript,
            [key,],
            [field, expected.IsNull ? "1" : "0", expected.IsNull ? RedisValue.EmptyString : expected, newValue,]
        ) ==
        1;

    /// <summary>Stamps the record's keep-alive; returns the updated record, or null if it does not exist.</summary>
    public static async Task<RedisValue> KeepAliveAsync(IDatabase db, RedisKey clients, RedisValue field, DateTime now)
    {
        var result = await db.ScriptEvaluateAsync(KeepAliveScript, [clients,], [field, RedisHash.Encode(now),]);
        return result.IsNull ? RedisValue.Null : (RedisValue)result;
    }

    /// <summary>Sets the record's client status; false if it does not exist.</summary>
    public static async Task<bool> SetStatusAsync(IDatabase db, RedisKey clients, RedisValue field, int status) =>
        (int)await db.ScriptEvaluateAsync(SetStatusScript, [clients,], [field, status,]) == 1;

    /// <summary>
    ///     Writes <paramref name="newRecord" /> over the stored record if it exists, has not seen a newer token than
    ///     <paramref name="fencingToken" />, and (given <paramref name="groupField" />) the lease still names
    ///     <paramref name="coordinatorId" /> with that token. The stored keep-alive and the fields <paramref name="keep" />
    ///     names are kept; the others come from <paramref name="newRecord" />.
    /// </summary>
    public static async Task<bool> WriteGuardedRecordAsync(
        IDatabase db,
        RedisKey clients,
        RedisKey lease,
        RedisValue field,
        string groupField,
        Guid coordinatorId,
        int fencingToken,
        object newRecord,
        KeptFields keep
    )
    {
        var template = JObject.FromObject(newRecord);
        template["LastKeepAlive"] = KeepAlivePlaceholder;
        if (keep.HasFlag(KeptFields.ClientStatus))
        {
            template["ClientStatus"] = StatusPlaceholder;
        }

        if (keep.HasFlag(KeptFields.FencingToken))
        {
            template["FencingToken"] = TokenPlaceholder;
        }

        return (int)await db.ScriptEvaluateAsync(
                   GuardedRecordScript,
                   [clients, lease,],
                   [
                       field, groupField ?? string.Empty, coordinatorId.ToString(), fencingToken,
                       template.ToString(Formatting.None),
                   ]
               ) ==
               1;
    }

    /// <summary>Deletes the entries that still hold the given values, while the lease is held; returns how many.</summary>
    public static async Task<int> DeleteUnchangedWhileHoldingLeaseAsync(
        IDatabase db,
        RedisKey clients,
        RedisKey lease,
        string groupField,
        Guid coordinatorId,
        int fencingToken,
        HashEntry[] entries
    )
    {
        var values = new RedisValue[3 + (entries.Length * 2)];
        values[0] = groupField;
        values[1] = coordinatorId.ToString();
        values[2] = fencingToken;
        for (var i = 0; i < entries.Length; i++)
        {
            values[3 + (i * 2)] = entries[i].Name;
            values[4 + (i * 2)] = entries[i].Value;
        }

        return (int)await db.ScriptEvaluateAsync(DeleteExpiredScript, [clients, lease,], values);
    }

    /// <summary>The fields of a stored client record a guarded write keeps (besides the keep-alive, always kept).</summary>
    [Flags]
    internal enum KeptFields
    {
        None = 0,
        ClientStatus = 1,
        FencingToken = 2,
    }
}
