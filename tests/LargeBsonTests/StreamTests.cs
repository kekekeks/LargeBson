using System.IO;
using LargeBson;
using Xunit;

namespace LargeBsonTests
{
    public class StreamTests
    {
        [Theory, InlineData(false), InlineData(true)]
        public void StreamsShouldBeDisposedAfterSerialization(bool async)
        {
            var ms = new MemoryStream(new byte[] {1, 2, 3}, true);
            Assert.True(ms.CanWrite);
            var bsonStream = LargeBsonSerializer.Serialize(new
            {
                Data = ms
            });
            if (async)
                bsonStream.CopyToAsync(new MemoryStream()).Wait();
            else
                bsonStream.CopyTo(new MemoryStream());
            // disposed check for memory stream
            Assert.False(ms.CanWrite);
            Assert.False(ms.CanRead);
        }

        [Fact]
        public void Streams_Should_Be_Disposed_When_Bson_Stream_Is_Disposed()
        {
            var ms = new MemoryStream(new byte[] {1, 2, 3}, true);
            Assert.True(ms.CanWrite);
            var bsonStream = LargeBsonSerializer.Serialize(new
            {
                Data = ms
            });
            bsonStream.Dispose();
            
            // disposed check for memory stream
            Assert.False(ms.CanWrite);
            Assert.False(ms.CanRead);
        }
    }
}