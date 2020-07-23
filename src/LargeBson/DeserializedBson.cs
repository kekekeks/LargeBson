using System;
using System.IO;

namespace LargeBson
{
    public class DeserializedBson : IDisposable
    {
        public object Data { get; }
        internal Stream InnerStream;

        internal DeserializedBson(object data, Stream innerStream)
        {
            Data = data;
            InnerStream = innerStream;
        }
        
        public void Dispose()
        {
            InnerStream.Dispose();
        }
    }
}