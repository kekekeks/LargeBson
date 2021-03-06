using System;
using System.Buffers;
using System.IO;
using LargeBson;
using Xunit;

namespace LargeBsonTests
{
    public class MemoryTests
    {
        class Mem : IMemoryOwner<byte>
        {
            public bool Disposed { get; private set; }
            public Mem(byte[] data)
            {
                Memory = new Memory<byte>(data);
            }
            
            public void Dispose()
            {
                Disposed = true;
            }

            public Memory<byte> Memory { get; }
        }
        

        
        [Theory, InlineData(false), InlineData(true)]
        public void Memory_Should_Be_Disposed_After_Serialization(bool async)
        {
            var m = new Mem(new byte[] {1, 2, 3});
            Assert.False(m.Disposed);
            var bsonStream = new LargeBsonSerializer().Serialize(new
            {
                Data = m
            });
            if (async)
                bsonStream.CopyToAsync(new MemoryStream()).Wait();
            else
                bsonStream.CopyTo(new MemoryStream());
            Assert.True(m.Disposed);
        }
        
        
        [Fact]
        public void Memory_Should_Be_Disposed_When_Bson_Stream_Is_Disposed()
        {
            var m = new Mem(new byte[] {1, 2, 3});
            Assert.False(m.Disposed);
            var bsonStream = new LargeBsonSerializer().Serialize(new
            {
                Data = m
            });
            bsonStream.Dispose();
            
            Assert.True(m.Disposed);
        }

        class ModelWithMemoryOwner
        {
            public IMemoryOwner<byte> Data { get; set; }
        }

        [Fact]
        public void Memory_Should_Be_Returned_To_Pool_When_Envelope_Is_Disposed()
        {
            var m = new Mem(new byte[] {1, 2, 3});
            var des =
                new LargeBsonSerializer().Deserialize(new LargeBsonSerializer().Serialize(new ModelWithMemoryOwner
                {
                    Data = m
                }), typeof(ModelWithMemoryOwner)).Result;
            var mem = ((ModelWithMemoryOwner) des.Data).Data;
            Assert.Equal(1, mem.Memory.Span[0]);
            des.Dispose();
            Assert.Throws<ObjectDisposedException>(() => mem.Memory.Span.ToArray());
        }
    }
}