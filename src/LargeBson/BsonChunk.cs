using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LargeBson
{
    unsafe struct BsonChunk : IDisposable
    {
        private int _arrayLength;
        private Stream _stream;
        private IMemoryOwner<byte> _memory;
        private byte[] _array;
        private int _offset;
        private fixed byte _fixed[16];
        private bool _isFixed;
        
        private ArrayPool<byte> _pool;

        public BsonChunk(Stream stream) : this()
        {
            _stream = stream;
        }

        public BsonChunk(IMemoryOwner<byte> memory) : this()
        {
            _memory = memory;
        }

        
        public BsonChunk(byte[] data, ArrayPool<byte> pool = null, int offset = 0, int? length = null) : this()
        {
            _arrayLength = length ?? data.Length;
            _array = data;
            _offset = offset;
            _pool = pool;
        }

        public BsonChunk(void* ptr, int len) : this()
        {
            if (len > 16)
                throw new ArgumentException();
            var s = (byte*) ptr;
            for (var c = 0; c < len; c++)
                _fixed[c] = s[c];
            _isFixed = true;
            _arrayLength = len;
        }

        public BsonChunk(int value) : this(&value, 4)
        {
            
        }
        
        public BsonChunk(byte value) : this(&value, 1)
        {
            
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            var read = 0;
            if (_isFixed)
            {
                for (var c = 0; c < count; c++)
                {
                    var soff = c + _offset;
                    if (soff >= _arrayLength)
                        break;
                    buffer[offset + c] = _fixed[soff];
                    read++;
                }

                _offset += read;

                return read;

            }

            if (_array != null)
            {
                for (var c = 0; c < count; c++)
                {
                    var soff = c + _offset;
                    if (soff >= _arrayLength)
                        break;
                    buffer[offset + c] = _array[soff];
                    read++;
                }

                _offset += read;
                return read;
            }

            if (_stream != null)
                return _stream.Read(buffer, offset, count);

            if (_memory != null)
            {
                var mem = _memory.Memory.Slice(_offset);
                read = Math.Min(mem.Length, count);
                mem.CopyTo(new Memory<byte>(buffer, offset, read));
                _offset += read;
                return read;
            }
            
            return 0;
        }

        public int Read(Span<byte> buffer)
        {
            var read = 0;
            var count = buffer.Length;
            if (_isFixed)
            {
                for (var c = 0; c < count; c++)
                {
                    var soff = c + _offset;
                    if (soff >= _arrayLength)
                        break;
                    buffer[c] = _fixed[soff];
                    read++;
                }

                _offset += read;

                return read;

            }

            if (_array != null)
            {
                for (var c = 0; c < count; c++)
                {
                    var soff = c + _offset;
                    if (soff >= _arrayLength)
                        break;
                    buffer[c] = _array[soff];
                    read++;
                }

                _offset += read;
                return read;
            }

            if (_stream != null)
                return _stream.Read(buffer);
            
            if (_memory != null)
            {
                var mem = _memory.Memory.Slice(_offset);
                read = Math.Min(mem.Length, count);
                mem.Span.CopyTo(buffer);
                _offset += read;
                return read;
            }

            return 0;
        }

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token)
        {
            if (_stream != null)
                return _stream.ReadAsync(buffer, token);

            return new ValueTask<int>(Read(buffer.Span));
        }

        public void Dispose()
        {
            if (_stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }

            if (_pool != null)
            {
                _pool.Return(_array);
                _pool = null;
            }

            if (_memory != null)
            {
                _memory.Dispose();
                _memory = null;
            }
        }
    }

    struct BsonChunks
    {
        private BsonChunk _c1, _c2, _c3;
        private int _count;

        public BsonChunks(BsonChunk c1, BsonChunk c2, BsonChunk c3)
        {
            _c1 = c1;
            _c2 = c2;
            _c3 = c3;
            _count = 3;
        }
        
        public BsonChunks(BsonChunk c1, BsonChunk c2)
        {
            _c1 = c1;
            _c2 = c2;
            _c3 = default;
            _count = 2;
        }
        
        public BsonChunks(BsonChunk c1)
        {
            _c1 = c1;
            _c2 = default;
            _c3 = default;
            _count = 1;
        }

        public static implicit operator BsonChunks(BsonChunk chunk) => new BsonChunks(chunk);
        
        public struct Enumerator
        {
            private BsonChunks _chunks;
            private int _index;

            public Enumerator(BsonChunks chunks)
            {
                _chunks = chunks;
                _index = -1;
            }

            public bool MoveNext()
            {
                _index++;
                return _chunks._count > _index;
            }

            public BsonChunk Current
            {
                get
                {
                    if (_index == 0)
                        return _chunks._c1;
                    if (_index == 1)
                        return _chunks._c2;
                    if (_index == 2)
                        return _chunks._c3;
                    throw new IndexOutOfRangeException();
                }
            }
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }
    }
}