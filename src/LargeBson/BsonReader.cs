using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.IO;
using NeoSmart.AsyncLock;

namespace LargeBson
{
    internal class BsonReader
    {
        static readonly RecyclableMemoryStreamManager StreamPool = new RecyclableMemoryStreamManager();
        public static async ValueTask<DeserializedBson> Deserialize(Stream s, Type type,
            TypeInfoCache typeInfoCache,
            RecyclableMemoryStreamManager streamPool = null)
        {
            var ms = new RecyclableMemoryStream(streamPool ?? StreamPool);
            var success = false;
            try
            {
                await s.CopyToAsync(ms);
                ms.Position = 0;
                using var ctx = new Context(ms, typeInfoCache);
                var (res, r) = await DeserializeCore(ctx, type, false);
                success = true;
                return new DeserializedBson(res, ms, ctx.Memories);
            }
            finally
            {
                if(!success)
                    ms.Dispose();
            }
        }

        public static async ValueTask<object> DeserializeInPlace(Stream s, Type type, TypeInfoCache typeInfoCache)
        {
            if (!s.CanSeek)
                throw new InvalidOperationException();
            using var ctx = new Context(s, typeInfoCache);
            return (await DeserializeCore(ctx, type, false)).res;
        }
        
        class Context : IDisposable
        {
            public byte[] Buffer = new byte[64];
            public SharedStreamHandler Share;
            public Stream Stream;
            public List<IMemoryOwner<byte>> Memories;
            private readonly TypeInfoCache _typeInfoCache;

            public Context(Stream stream, TypeInfoCache typeInfoCache)
            {
                Stream = stream;
                _typeInfoCache = typeInfoCache;
                Share = new SharedStreamHandler(stream);
            }

            public TypeInformation GetType(Type t) => _typeInfoCache.Get(t);

            public void AddMemory(IMemoryOwner<byte> mem)
            {
                if (Memories == null)
                    Memories = new List<IMemoryOwner<byte>>();
                Memories.Add(mem);
            }
            
            public async ValueTask ReadExact(byte[] buffer, int length)
            {
                var off = 0;
                while (length > 0)
                {
                    var read = await Stream.ReadAsync(buffer, off, length);
                    if (read == 0)
                        throw new EndOfStreamException();
                    off += read;
                    length -= read;
                }
            }
            
            public async ValueTask ReadExact(Memory<byte> buffer)
            {
                var off = 0;
                var length = buffer.Length;
                while (length > 0)
                {
                    var read = await Stream.ReadAsync(buffer.Slice(off, length));
                    if (read == 0)
                        throw new EndOfStreamException();
                    off += read;
                    length -= read;
                }
            }
            
            public async ValueTask<int> ReadInt()
            {
                await ReadExact(Buffer, 4);
                return BitConverter.ToInt32(Buffer, 0);
            }

            public async ValueTask<long> ReadLong()
            {
                await ReadExact(Buffer, 8);
                return BitConverter.ToInt64(Buffer, 0);
            }

            
            public async ValueTask<byte> ReadByte()
            {
                if ((await Stream.ReadAsync(Buffer, 0, 1)) == 0)
                    throw new EndOfStreamException();
                return Buffer[0];
            }

            public async ValueTask<ArraySegment<byte>> ReadCString()
            {
                // Note: buffer is reused with ReadByte, so we can't use the first element
                for (var c = 0; c < Buffer.Length - 1; c++)
                {
                    Buffer[c + 1] = await ReadByte();
                    if (Buffer[c + 1] == 0)
                        return new ArraySegment<byte>(Buffer, 1, c + 1);
                }

                throw new InvalidOperationException("Property name is too long");
            }

            public async ValueTask<Guid> ReadGuid()
            {
                await ReadExact(Buffer, 16);
                return new Guid(Buffer.AsSpan().Slice(0, 16));
            }

            public void Dispose()
            {
                Share.DisposeIfNoRefs();
            }
        }


        private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Shared;
        
        static async ValueTask<(object res, int read)> DeserializeCore(Context ctx, Type t, bool array)
        {
            var nfo = ctx.GetType(t);
            if (nfo.IsBsonArray != array)
                throw new InvalidOperationException("Attempted to read object as array or vice versa");

            var writer = nfo.CreateWriteAdapter();

            var expectedLen = await ctx.ReadInt();
            var totalLen = expectedLen - 4;
            while (totalLen > 1)
            {
                var type = (BsonType)await ctx.ReadByte();
                totalLen--;
                
                var name = await ctx.ReadCString();
                totalLen -= name.Count;

                writer.SelectProperty(name);
                if (type == BsonType.Int32)
                {
                    var i = await ctx.ReadInt();
                    totalLen -= 4;
                    writer.WriteValue(i);
                }
                else if (type == BsonType.Int64)
                {
                    var i = await ctx.ReadLong();
                    totalLen -= 8;
                    writer.WriteValue(i);
                }
                else if (type == BsonType.Null)
                    writer.WriteValue(null);
                else if (type == BsonType.Boolean)
                {
                    var b = await ctx.ReadByte() != 0;
                    totalLen--;
                    writer.WriteValue(b);
                }
                else if (type == BsonType.String)
                {
                    var slen = await ctx.ReadInt();
                    totalLen -= 4;
                    if (slen > totalLen || slen > 0x1000 || slen == 0)
                        throw new ArgumentException("Invalid string len " + slen);
                    var pooled = Pool.Rent(slen);
                    await ctx.ReadExact(pooled, slen);
                    totalLen -= slen;
                    if (pooled[slen - 1] != 0)
                    {
                        Pool.Return(pooled);
                        throw new ArgumentException("String isn't followed by a null byte");
                    }

                    var s = Encoding.UTF8.GetString(pooled, 0, slen - 1);
                    writer.WriteValue(s);
                }
                else if (type == BsonType.Array || type == BsonType.Object)
                {
                    var (res, read) = await DeserializeCore(ctx, writer.CurrentPropertyType, type == BsonType.Array);
                    totalLen -= read;
                    writer.WriteValue(res);
                }
                else if (type == BsonType.Binary)
                {
                    var blen = await ctx.ReadInt();
                    var btype = await ctx.ReadByte();
                    totalLen -= 5;

                    if (btype == 4)
                    {
                        if (blen != 16)
                            throw new ArgumentException("Invalid GUID size: " + blen);
                        var guid = await ctx.ReadGuid();
                        totalLen -= 16;
                        writer.WriteValue(guid);
                    }
                    else if (writer.CurrentPropertyType == typeof(Stream))
                    {
                        var slice = new StreamSlice(ctx.Stream, ctx.Stream.Position, blen, ctx.Share);
                        ctx.Stream.Position += blen;
                        writer.WriteValue(slice);
                        totalLen -= blen;
                        //
                    }
                    else if (writer.CurrentPropertyType == typeof(byte[]))
                    {
                        if (blen > 0x10000)
                            throw new ArgumentException("byte[] blob is too large");
                        var data = new byte[blen];
                        await ctx.ReadExact(data, blen);
                        totalLen -= blen;
                        writer.WriteValue(data);

                    }
                    else if(writer.CurrentPropertyType == typeof(IMemoryOwner<byte>))
                    {
                        var mem = new MemoryWrapper(MemoryPool<byte>.Shared.Rent(blen), blen);
                        ctx.AddMemory(mem);
                        var rsuccess = false;
                        try
                        {
                            await ctx.ReadExact(mem.Memory);
                            rsuccess = true;
                        }
                        finally
                        {
                            if(!rsuccess)
                                mem.Dispose();
                        }

                        totalLen -= blen;
                        writer.WriteValue(mem);
                    }
                    else
                        throw new ArgumentException("Unable to deserialize binary to " + writer.CurrentPropertyType);
                }
                else
                    throw new ArgumentException("Unsupported BSON type " + type);
            }

            if (totalLen != 1 || await ctx.ReadByte() != 0)
                throw new ArgumentException("Unexpected length of object");
            return (writer.CreateInstance(), expectedLen);
        }

        private static void Add(List<object> targetList, object targetObject, PropertyInfo propertyInfo,
            ArraySegment<byte> name,             object v)
        {
            if (targetList != null)
                targetList.Add(v);
            else
                propertyInfo.Set(targetObject, v);
        }
    }
}