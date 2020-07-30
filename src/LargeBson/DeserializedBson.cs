using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;

namespace LargeBson
{
    public class DeserializedBson : IDisposable
    {
        public object Data { get; }
        internal List<IMemoryOwner<byte>> Memories;
        internal Stream InnerStream;

        internal DeserializedBson(object data, Stream innerStream, List<IMemoryOwner<byte>> memories)
        {
            Data = data;
            Memories = memories;
            InnerStream = innerStream;
        }

        public void Dispose()
        {
            InnerStream.Dispose();
            if (Memories != null)
                foreach (var m in Memories)
                    m.Dispose();
            Memories = null;
        }
    }
}