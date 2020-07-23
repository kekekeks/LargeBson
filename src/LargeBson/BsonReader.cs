using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NeoSmart.AsyncLock;

namespace LargeBson
{
    internal class BsonReader
    {
        public static async ValueTask<DeserializedBson> Deserialize(Stream s, Type type)
        {
            var ms = new MemoryStream();
            var success = false;
            try
            {
                await s.CopyToAsync(ms);
                ms.Position = 0;
                var (res, r) = await DeserializeWrapped(new Context(ms), type, false);
                success = true;
                return new DeserializedBson(res, ms);
            }
            finally
            {
                if(!success)
                    ms.Dispose();
            }
        }

        public static async ValueTask<object> DeserializeInPlace(Stream s, Type type)
        {
            if (!s.CanSeek)
                throw new InvalidOperationException();
            return (await DeserializeWrapped(new Context(s), type, false)).res;
        }
        
        class Context
        {
            public byte[] Buffer = new byte[64];
            public AsyncLock Lock = new AsyncLock();
            public Stream Stream;

            public Context(Stream stream)
            {
                Stream = stream;
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
        }


        private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Shared;

        static async ValueTask<(object res, int read)> DeserializeWrapped(Context ctx, Type t, bool array)
        {
            return await DeserializeCore(ctx, t, array);
        }
        static async ValueTask<(object res, int read)> DeserializeCore(Context ctx, Type t, bool array)
        {
            List<object> targetList = null;
            object targetObject = null;
            TypePropertyList typeInfo = null;
            if (array)
                targetList = new List<object>();
            else
            {
                targetObject = Activator.CreateInstance(t);
                typeInfo = TypePropertyList.Get(t);
            }

            var expectedLen = await ctx.ReadInt();
            var totalLen = expectedLen - 4;
            while (totalLen > 1)
            {
                var type = (BsonType)await ctx.ReadByte();
                totalLen--;
                
                var name = await ctx.ReadCString();
                totalLen -= name.Count;

                var prop = typeInfo?.GetProperty(name);

                if (type == BsonType.Int32)
                {
                    var i = await ctx.ReadInt();
                    totalLen -= 4;
                    Add(targetList, targetObject, prop, name, i);
                }
                else if (type == BsonType.Int64)
                {
                    var i = await ctx.ReadLong();
                    totalLen -= 8;
                    Add(targetList, targetObject, prop, name, i);
                }
                else if (type == BsonType.Null)
                    Add(targetList, targetObject, prop, name, null);
                else if (type == BsonType.Boolean)
                {
                    var b = await ctx.ReadByte() != 0;
                    totalLen--;
                    Add(targetList, targetObject, prop, name, b);
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
                    Add(targetList, targetObject, prop, name, s);
                }
                else if (type == BsonType.Array)
                {
                    throw new ArgumentException("Array deserialization is not supported yet");
                }
                else if (type == BsonType.Object)
                {
                    var (res, read) = await DeserializeCore(ctx, prop.Type, false);
                    totalLen -= read;
                    prop.Set(targetObject, res);
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
                        Add(targetList, targetObject, prop, name, guid);
                    }
                    
                    if (prop.Type == typeof(Stream))
                    {
                        var slice = new StreamSlice(ctx.Stream, ctx.Stream.Position, blen, ctx.Lock);
                        ctx.Stream.Position += blen;
                        prop.Set(targetObject, slice);
                        totalLen -= blen;
                        //
                    }
                    else if (prop.Type == typeof(byte[]))
                    {
                        if (blen > 0x10000)
                            throw new ArgumentException("byte[] blob is too large");
                        var data = new byte[blen];
                        await ctx.ReadExact(data, blen);
                        totalLen -= blen;
                        Add(targetList, targetObject, prop, name, data);

                    }
                    else
                        throw new ArgumentException("Unable to deserialize binary to " + prop.Type);
                }
                else
                    throw new ArgumentException("Unsupported BSON type " + type);
            }

            if (totalLen != 1 || await ctx.ReadByte() != 0)
                throw new ArgumentException("Unexpected length of object");
            return (targetObject, expectedLen);
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