using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LargeBson
{
    static class BsonBuilder
    {

        public static Stream Build(object value)
        {
            var root = BuildDocument(value);
            var hs = new HashSet<IDisposable>();
            CollectDisposableStreams(hs, BsonToken.FromDocument(root));
            return new BsonStream(root.Length, BuildChunks(root).GetEnumerator(), hs.ToList());
        }

        struct State
        {
            public BsonDocument Document;
            public BsonArray Array;
            public int Index;
        }

        static void CollectDisposableStreams(HashSet<IDisposable> hs, BsonToken token)
        {
            if (token.Stream != null)
                hs.Add(token.Stream);
            if (token.Memory != null)
                hs.Add(token.Memory);
            else if (token.Document != null)
                foreach (var p in token.Document.Properties)
                    CollectDisposableStreams(hs, p.Token);
            else if (token.Type == BsonType.Array)
                foreach (var e in token.Array.Elements)
                    CollectDisposableStreams(hs, e);
        }

        static IEnumerable<BsonChunk> BuildChunks(BsonDocument root)
        {
            var stack = new Stack<State>();
            var state = new State
            {
                Document = root
            };
            
            yield return new BsonChunk(root.Length);
            
            while (true)
            {
                BsonToken writeToken;
                if (state.Document != null)
                {
                    if (state.Index >= state.Document.Properties.Count)
                    {
                        yield return new BsonChunk((byte) 0);
                        if(stack.Count == 0)
                            break;
                        state = stack.Pop();
                        continue;
                    }
                    else
                    {
                        var prop = state.Document.Properties[state.Index];
                        state.Index++;
                        yield return new BsonChunk((byte)prop.Token.Type);
                        yield return new BsonChunk(prop.Name);
                        writeToken = prop.Token;
                    }
                }
                else
                {
                    if (state.Index >= state.Array.Elements.Count)
                    {
                        yield return new BsonChunk((byte) 0);
                        if(stack.Count == 0)
                            break;
                        state = stack.Pop();
                        continue;
                    }
                    else
                    {
                        var element = state.Array.Elements[state.Index];
                        yield return new BsonChunk((byte)element.Type);
                        yield return BsonArray.ElementPrefixAtIndex(state.Index);
                        state.Index++;
                        writeToken = element;
                    }
                }

                if (writeToken.Type == BsonType.Object)
                {
                    yield return new BsonChunk(writeToken.Document.Length);
                    stack.Push(state);
                    state = new State {Document = writeToken.Document};
                    continue;
                }
                else if (writeToken.Type == BsonType.Array)
                {
                    yield return new BsonChunk(writeToken.Array.Length);
                    stack.Push(state);
                    state = new State {Array =  writeToken.Array};
                    continue;
                }
                else
                {
                    foreach (var ch in writeToken.CreateChunks())
                        yield return ch;
                }
                
            }


        }
        
        static BsonDocument BuildDocument(object value)
        {
            if (value is IEnumerable || value == null || value is string || value.GetType().IsPrimitive ||
                value is Guid)
                throw new InvalidOperationException();
            return new BsonDocument(value);
        }
        
        
        class BsonDocument
        {
            public int Length;
            public List<BsonProperty> Properties = new List<BsonProperty>();

            public BsonDocument(object value)
            {
                var t = TypeInformation.Get(value.GetType());
                foreach (var p in t.Properties)
                    if (p.CanRead)
                    {
                        var prop = new BsonProperty(p.CstringName, BsonToken.Create(p.Get(value)));
                        Properties.Add(prop);
                        Length += prop.Length;
                    }

                Length += 5;
            }
        }

        struct BsonArray
        {
            static int[] SizeTable =
            {
                9, 99, 999, 9999, 99999, 999999, 9999999,
                99999999, 999999999, int.MaxValue
            };

            static int StringSize(int x)
            {
                for (int i = 0;; i++)
                    if (x <= SizeTable[i])
                        return i + 1;
            }

            public List<BsonToken> Elements;
            public int InnerLength { get; }
            public int Length { get; }

            public BsonArray(List<BsonToken> elements)
            {
                Elements = elements;
                var len = 0;
                for (var c = 0; c < elements.Count; c++)
                    len += 2 + StringSize(c) + elements[c].Length;
                InnerLength = len;
                Length = len + 5;
            }


            static unsafe int Itoa(int value, byte* buf)
            {
                var len = 0;
                if (value == 0)
                {
                    buf[0] = 48;
                    buf[1] = 0;
                    return 2;
                }

                while (value != 0)
                {
                    int rem = value % 10;
                    buf[len] = (byte) (48 + rem);
                    len++;
                    value = value / 10;
                }
                buf[len] = 0;

                
                for (var c = 0; c < len / 2; c++)
                {
                    var mirror = len - c - 1;
                    var saved = buf[mirror];
                    buf[mirror] = buf[c];
                    buf[c] = saved;
                }
                

                return len + 1;
            }

            public static unsafe BsonChunk ElementPrefixAtIndex(int i)
            {
                var size = StringSize(i);
                var buffer = stackalloc byte[16];

                return new BsonChunk(buffer, Itoa(i, buffer));

            }
            
            public static BsonArray FromEnumerable(IEnumerable en)
            {
                var lst = new List<BsonToken>();
                foreach (var e in en)
                    lst.Add(BsonToken.Create(e));
                return new BsonArray(lst);
            }
        }


        struct BsonProperty
        {
            public byte[] Name { get; }
            public BsonToken Token { get; }
            public int Length { get; }

            public BsonProperty(byte[] name, BsonToken token)
            {
                Name = name;
                Token = token;
                Length = name.Length + 1 + token.Length;
            }
        }

        unsafe struct BsonToken
        {
            public BsonArray Array;
            public BsonDocument Document;
            public string String;
            public int Length;
            public BsonType Type;
            public int Int;
            public long Long;
            public byte[] Data;
            public Stream Stream;
            public IMemoryOwner<byte> Memory;
            public byte BinaryType;
            public Guid Guid;
            public bool Boolean;


            public static BsonToken Create(object value)
            {
                if (value == null)
                    return BsonToken.Null;
                if (value is bool b)
                    return FromBoolean(b);
                if (value is string s)
                    return BsonToken.FromString(s);
                if (value is int i)
                    return BsonToken.FromInt(i);
                if (value is long l)
                    return BsonToken.FromLong(l);
                if (value is Guid g)
                    return BsonToken.FromGuid(g);
                if (value is byte[] data)
                    return BsonToken.FromData(data);
                if (value is Stream stream)
                    return BsonToken.FromData(stream);
                if (value is IMemoryOwner<byte> memory)
                    return BsonToken.FromData(memory);
                
                var t = value.GetType();
                if (t.IsPrimitive)
                    throw new InvalidOperationException();
                if (value is IEnumerable en)
                    return BsonToken.FromArray(BsonArray.FromEnumerable(en));
                return FromDocument(new BsonDocument(value));

            }
            
            public static BsonToken FromString(string s)
            {
                if (s == null)
                    throw new NullReferenceException();
                return new BsonToken
                {
                    String = s,
                    Length = Encoding.UTF8.GetByteCount(s) + 5,
                    Type = BsonType.String
                };
            }


            public static BsonToken FromDocument(BsonDocument doc)
            {
                if (doc == null)
                    throw new NullReferenceException();
                return new BsonToken
                {
                    Document = doc,
                    Length = doc.Length,
                    Type = BsonType.Object
                };
            }

            public static BsonToken FromArray(BsonArray arr) =>
                new BsonToken
                {
                    Array = arr,
                    Length = arr.Length,
                    Type = BsonType.Array
                };

            public static BsonToken FromBoolean(bool b) => new BsonToken
            {
                Boolean = b,
                Length = 1,
                Type = BsonType.Boolean
            };
            
            public static BsonToken FromInt(int i) => new BsonToken
            {
                Int = i,
                Length = 4,
                Type = BsonType.Int32
            };

            public static BsonToken FromLong(long l) => new BsonToken
            {
                Long = l,
                Length = 8,
                Type = BsonType.Int64
            };

            public static BsonToken FromData(byte[] data) => new BsonToken
            {
                Data = data,
                Length = 5 + data.Length,
                Type = BsonType.Binary
            };

            public static BsonToken FromData(Stream stream) => new BsonToken
            {
                Stream = stream,
                Length = (int) (5 + stream.Length),
                Type = BsonType.Binary
            };
            
            public static BsonToken FromData(IMemoryOwner<byte> memory) => new BsonToken
            {
                Memory = memory,
                Length = (int) (5 + memory.Memory.Length),
                Type = BsonType.Binary
            };

            public static BsonToken FromGuid(Guid guid) => new BsonToken
            {
                Guid = guid,
                Length = (int) (5 + 16),
                Type = BsonType.Binary,
                BinaryType = 4
            };


            public static BsonToken Null => new BsonToken {Type = BsonType.Null};

            public BsonChunks CreateChunks()
            {
                if (Type == BsonType.Boolean)
                {
                    var b = Boolean ? 1 : 0;
                    return new BsonChunk(&b, 1);
                }
                if (Type == BsonType.Int32)
                {
                    var i = Int;
                    return new BsonChunk(&i, 4);
                }

                if (Type == BsonType.Int64)
                {
                    var i = Long;
                    return new BsonChunk(&i, 8);
                }

                if (Type == BsonType.String)
                {
                    var len = Length - 4;
                    var pooled = ArrayPool<byte>.Shared.Rent(len + 1);
                    Encoding.UTF8.GetBytes(String, 0, String.Length, pooled, 0);
                    pooled[len] = 0;

                    return new BsonChunks(new BsonChunk(&len, 4),
                        new BsonChunk(pooled, ArrayPool<byte>.Shared, 0, len));
                }

                if (Type == BsonType.Binary)
                {
                    BsonChunk BinaryLen(int len, byte type)
                    {
                        var prefix = stackalloc byte[5];
                        *((int*) prefix) = len;
                        prefix[4] = type;
                        return new BsonChunk(prefix, 5);
                    }

                    if (BinaryType == 4)
                    {
                        var g = Guid;
                        return new BsonChunk(&g, 16);
                    }

                    if (Data != null)
                    {
                        return new BsonChunks(BinaryLen(Data.Length, BinaryType),
                            new BsonChunk(Data));
                    }
                    else if (Stream != null)
                        return new BsonChunks(BinaryLen((int) Stream.Length, BinaryType),
                            new BsonChunk(Stream));
                    else if(Memory != null)
                        return new BsonChunks(BinaryLen((int)Memory.Memory.Length, BinaryType),
                            new BsonChunk(Memory));
                }

                if (Type == BsonType.Null)
                    return default;

                throw new InvalidOperationException();

            }
        }
    }
}