using System;
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

        class TwoStreamModel
        {
            public Stream Stream1 { get; set; }
            public Stream Stream2 { get; set; }
        }

        [Fact]
        public void Source_Stream_Should_Be_Disposed_With_Envelope()
        {
            DeserializedBson bs;
            using (var bsonStream = LargeBsonSerializer.Serialize(new TwoStreamModel
            {
                Stream1 = new MemoryStream(new byte[] {1, 2, 3}),
                Stream2 = new MemoryStream(new byte[] {3, 2, 1}),
            }))
                bs = LargeBsonSerializer.Deserialize(bsonStream, typeof(TwoStreamModel)).Result;
            
            Assert.True(bs.InnerStream.CanRead);
            ((TwoStreamModel) bs.Data).Stream1.ReadByte();
            bs.Dispose();
            Assert.False(bs.InnerStream.CanRead);
            Assert.Throws<ObjectDisposedException>(() => ((TwoStreamModel) bs.Data).Stream1.ReadByte());
        }
        
        [Fact]
        public void Source_Stream_Should_Be_Disposed_With_Last_Consumer()
        {
            DeserializedBson bs;
            using (var bsonStream = LargeBsonSerializer.Serialize(new TwoStreamModel
            {
                Stream1 = new MemoryStream(new byte[] {1, 2, 3}),
                Stream2 = new MemoryStream(new byte[] {3, 2, 1}),
            }))
                bs = LargeBsonSerializer.Deserialize(bsonStream, typeof(TwoStreamModel)).Result;
            
            Assert.True(bs.InnerStream.CanRead);
            var mdl = ((TwoStreamModel) bs.Data);
            mdl.Stream1.ReadByte();
            mdl.Stream1.Dispose();
            Assert.True(bs.InnerStream.CanRead);
            
            mdl.Stream2.ReadByte();
            mdl.Stream2.Dispose();
            Assert.False(bs.InnerStream.CanRead);
            
            Assert.Throws<ObjectDisposedException>(() => ((TwoStreamModel) bs.Data).Stream2.ReadByte());
        }

        class NoStreamModel
        {
            public int Foo { get; set; }
        }
        
        [Fact]
        public void Source_Stream_Should_Be_Disposed_When_There_Are_No_Consumer_Streams()
        {
            DeserializedBson bs;
            using (var bsonStream = LargeBsonSerializer.Serialize(new NoStreamModel
            {
                Foo = 123
            }))
                bs = LargeBsonSerializer.Deserialize(bsonStream, typeof(NoStreamModel)).Result;
            
            Assert.False(bs.InnerStream.CanRead);
        }
    }
}