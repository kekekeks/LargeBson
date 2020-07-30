using System;
using System.Buffers;

namespace LargeBson
{
    class MemoryWrapper : IMemoryOwner<byte>
    {
        private IMemoryOwner<byte> _inner;
        private readonly int _len;

        public MemoryWrapper(IMemoryOwner<byte> inner, int len)
        {
            _inner = inner;
            _len = len;
        }
        
        public void Dispose()
        {
            _inner?.Dispose();
            _inner = null;
        }

        public Memory<byte> Memory
        {
            get
            {
                if (_inner == null)
                    throw new ObjectDisposedException("memory");
                return _inner.Memory.Slice(0, _len);
            }
        }
    }
}