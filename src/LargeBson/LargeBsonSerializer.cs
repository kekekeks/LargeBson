using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.IO;

[assembly: InternalsVisibleTo("LargeBsonTests")]
namespace LargeBson
{
    public class LargeBsonSerializer
    {
        private readonly TypeInfoCache _typeInfoCache;

        public LargeBsonSerializer(LargeBsonSettings settings = null)
        {
            _typeInfoCache = new TypeInfoCache(settings ?? new LargeBsonSettings());
        }
        
        public Stream Serialize(object data)
        {
            return BsonBuilder.Build(data, _typeInfoCache);
        }

        public ValueTask<DeserializedBson> Deserialize(Stream s, Type t, RecyclableMemoryStreamManager streamPool = null)
        {
            return BsonReader.Deserialize(s, t, _typeInfoCache, streamPool);
        }
    }


    internal enum BsonType
    {
        Double = 1,
        String = 2,
        Object = 3,
        Array = 4,
        Binary = 5,
        Undefined = 6,
        ObjectId = 7,
        Boolean = 8,
        DateTime = 9,
        Null = 10,
        Regex = 11,
        Reference = 12,
        Code = 13,
        Symbol = 14,
        ScopedCode = 15,
        Int32 = 16,
        Timestamp = 17,
        Int64 = 18,
    }
}