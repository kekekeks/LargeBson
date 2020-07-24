using System;
using System.IO;
using LargeBson;
using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
using Xunit;

namespace LargeBsonTests
{
    public class JsonNetWriteComparisonTests
    {

        static void CompareBytes(byte[] left, byte[] right)
        {
            var maxLen = Math.Max(left.Length, right.Length);
            for (var c = 0; c < maxLen; c++)
            {
                if (c >= left.Length || c >= right.Length)
                    throw new Exception("Size difference at " + c);
                if (left[c] != right[c])
                    throw new Exception("Data different at " + c);
            }
        }

        static void Compare(object jnet, Func<object> lb)
        {
            var ms = new MemoryStream();
            using (var bsonWriter = new BsonWriter(ms))
            {
                new JsonSerializer().Serialize(bsonWriter, jnet);
            }

            var jsonNet = ms.ToArray();
            for (var c = 0; c < 2; c++)
            {
                ms = new MemoryStream();
                using (var s = new LargeBsonSerializer().Serialize(lb()))
                    if (c == 0)
                        s.CopyTo(ms);
                    else
                        s.CopyToAsync(ms);
                var largeBson = ms.ToArray();
                CompareBytes(jsonNet, largeBson);
            }
        }

        static void Compare(object root) => Compare(root, () => root);

        [Fact]
        public void SerializerProduceSameResultsAsJsonNet()
        {
            Compare(new object());
            Compare(new
            {
                Test = "123"
            });
            Compare(new
            {
                Test = "123",
                Test2 = true,
                Test3 = 1,
                Test4 = (long) 2
            });
            Compare(new
            {
                Nested = new
                {
                    Test = "123"
                }
            });
            Compare(new
            {
                Array = new[] {1, 2, 3, 4, 5, 6}
            });
            Compare(new
            {
                Data = new byte[] {1, 2, 3, 4, 5}
            });
            var blob = new byte[1024 * 1024 * 16];
            new Random().NextBytes(blob);

            Compare(new
            {
                Data = blob

            }, () => new {Data = new MemoryStream(blob)});

        }
    }
}