using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LargeBson;
using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
using Xunit;

namespace LargeBsonTests
{
    public class JsonNetReadCompatTests
    {
        public class Model
        {
            public Model SubModel { get; set; }
            public List<Model> ModelList { get; set; }
            public int TestInt { get; set; }
            public List<int> IntList { get; set; }
            public int[] IntArray { get; set; }
            public long TestLong { get; set; }
            public string TestString { get; set; }
            public byte[] TestBytes { get; set; }
            [JsonConverter(typeof(StreamToByteConverter))]
            public Stream TestStream { get; set; }
        }

        MemoryStream JsonNetSerialize(object o)
        {
            var ms = new MemoryStream();
            using (var bsonWriter = new BsonWriter(ms))
            {
                new JsonSerializer().Serialize(bsonWriter, o);
            }

            return new MemoryStream(ms.ToArray());
        }
        
        void Check(object o)
        {
            var serializedOriginal = JsonNetSerialize(o);

            serializedOriginal.Position = 0;
            var deserialized = new LargeBsonSerializer().Deserialize(serializedOriginal, o.GetType()).Result;

            var check = JsonNetSerialize(deserialized.Data);
            Assert.True(serializedOriginal.ToArray().SequenceEqual(check.ToArray()));
        }
        
        [Fact]
        public void CanConsumeDataFromJsonNet()
        {
            Check(new Model
            {
                SubModel = new Model()
                {
                    TestBytes = new byte[] {1, 2, 3},
                    TestStream = new MemoryStream(new byte[] {3, 2, 1}),
                    TestInt = 1234,
                    TestLong = 12345,
                    TestString = "Lalalalala"
                },
                TestBytes = new byte[] {0, 1, 2, 3},
                TestStream = new MemoryStream(new byte[] {3, 2, 1, 0}),
                TestInt = 12340,
                TestLong = 123450,
                TestString = "testtesttest",
                IntList = new List<int> {1, 2, 3, 4, 5},
                IntArray = new int[] {5, 4, 3, 2, 1},
                ModelList = new List<Model>
                {
                    new Model
                    {
                        TestInt = 123
                    }
                }

            });
        }
        
    }

    public class StreamToByteConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var ms = new MemoryStream();
            ((Stream)value).CopyTo(ms);
            serializer.Serialize(writer, ms.ToArray());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) 
            => throw new NotSupportedException();

        public override bool CanConvert(Type objectType)
        {
            return typeof(Stream).IsAssignableFrom(objectType);
        }
    }
}