namespace Rebalancer.ZooKeeper.Zk;

using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

public static class JSONSerializer<TType> where TType : class
{
    /// <summary>
    ///     Serializes an object to JSON
    /// </summary>
    public static string Serialize(TType instance)
    {
        DataContractJsonSerializer serializer = new(typeof(TType));
        using (MemoryStream stream = new())
        {
            serializer.WriteObject(stream, instance);
            var ser = Encoding.UTF8.GetString(stream.ToArray());
            return ser;
        }
    }

    /// <summary>
    ///     DeSerializes an object from JSON
    /// </summary>
    public static TType DeSerialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        using (MemoryStream stream = new(Encoding.UTF8.GetBytes(json)))
        {
            stream.Position = 0;
            DataContractJsonSerializer serializer = new(typeof(TType));
            return serializer.ReadObject(stream) as TType;
        }
    }
}
