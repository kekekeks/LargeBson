using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace LargeBson
{
    public static class LargeBsonSerializer
    {
        public static Stream Serialize(object data)
        {
            return BsonBuilder.Build(data);
        }

        public static ValueTask<DeserializedBson> Deserialize(Stream s, Type t)
        {
            return BsonReader.Deserialize(s, t);
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