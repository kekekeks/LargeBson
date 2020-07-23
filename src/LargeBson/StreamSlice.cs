using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NeoSmart.AsyncLock;

namespace LargeBson
{
    class StreamSlice : Stream
    {
        private readonly Stream _inner;
        private readonly long _from;
        private readonly long _len;
        private readonly AsyncLock _l;
        private long _position;

        public StreamSlice(Stream inner, long from, long len, AsyncLock l)
        {
            _inner = inner;
            _from = @from;
            _len = len;
            _l = l;
        }

        public override void Flush() => throw new NotSupportedException();

        void Sync()
        {
            _inner.Position = _from + _position;
        }

        int GetLen(int count) => (int) Math.Min(count, _len - _position);
        
        public override int Read(byte[] buffer, int offset, int count)
        {
            using (_l.Lock())
            {
                Sync();
                count = GetLen(count);
                if (count == 0)
                    return 0;
                var read =  _inner.Read(buffer, offset, count);
                _position += read;
                return read;
            }
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            using (await _l.LockAsync())
            {
                Sync();
                count = GetLen(count);
                if (count == 0)
                    return 0;
                var read = await _inner.ReadAsync(buffer, offset, count, cancellationToken);
                _position += read;
                return read;
            }
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
        {
            using (await _l.LockAsync())
            {
                Sync();
                var count = GetLen(buffer.Length);
                if (count == 0)
                    return 0;
                buffer = buffer.Slice(0, count);
                var read = await _inner.ReadAsync(buffer, cancellationToken);
                _position += read;
                return read;
            }
        }

        public override int Read(Span<byte> buffer)
        {
            using (_l.Lock())
            {
                Sync();
                var count = GetLen(buffer.Length);
                if (count == 0)
                    return 0;
                buffer = buffer.Slice(0, count);
                var read = _inner.Read(buffer);
                _position += read;
                return read;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Begin)
                Position = offset;
            if (origin == SeekOrigin.Current)
                Position += offset;
            if (origin == SeekOrigin.End)
                Position = _len + offset;
            return _position;
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite { get; }
        public override long Length => _len;
        public override long Position
        {
            get => _position;
            set
            {
                if (_position < 0 || _position > _len)
                    throw new ArgumentException();
                _position = value;
            }
        }
    }
}