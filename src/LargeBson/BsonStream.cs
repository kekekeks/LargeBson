using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LargeBson
{
    class BsonStream : Stream
    {
        private readonly IEnumerator<BsonChunk> _en;
        private List<IDisposable> _disposables;
        private BsonChunk _currentChunk;
        private bool _hasChunk;

        public BsonStream(long length, IEnumerator<BsonChunk> en,
            List<IDisposable> disposables)
        {
            _en = en;
            _disposables = disposables;
            Length = length;
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length { get; }

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }


        public override int Read(byte[] buffer, int offset, int count)
        {
            var readTotal = 0;
            while (readTotal < count)
            {
                if (!_hasChunk)
                {
                    _hasChunk = _en.MoveNext();
                    if (!_hasChunk)
                        return readTotal;
                    _currentChunk = _en.Current;
                }
                
                var read = _currentChunk.Read(buffer, offset, count);
                if (read == 0)
                {
                    _currentChunk.Dispose();
                    _hasChunk = false;
                    continue;
                }
                readTotal += read;
                offset += read;
                count -= read;
            }
            return readTotal;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
        {
            var readTotal = 0;
            var count = buffer.Length;
            var offset = 0;
            while (readTotal < count)
            {
                if (!_hasChunk)
                {
                    _hasChunk = _en.MoveNext();
                    if (!_hasChunk)
                        return readTotal;
                    _currentChunk = _en.Current;
                }

                var read = await _currentChunk.ReadAsync(buffer.Slice(offset, count), cancellationToken);
                if (read == 0)
                {
                    _currentChunk.Dispose();
                    _hasChunk = false;
                    continue;
                }
                readTotal += read;
                offset += read;
                count -= read;
            }
            return readTotal;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var readTotal = 0;
            while (readTotal < count)
            {
                if (!_hasChunk)
                {
                    _hasChunk = _en.MoveNext();
                    if (!_hasChunk)
                        return readTotal;
                    _currentChunk = _en.Current;
                }

                var read = await _currentChunk.ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken);
                if (read == 0)
                {
                    _currentChunk.Dispose();
                    _hasChunk = false;
                    continue;
                }
                readTotal += read;
                offset += read;
                count -= read;
            }
            return readTotal;
        }

        protected override void Dispose(bool disposing)
        {
            _currentChunk.Dispose();
            if(_disposables!=null)
                foreach (var d in _disposables)
                    d.Dispose();
            _disposables = null;
            base.Dispose(disposing);
        }
    }
}