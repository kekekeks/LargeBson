using System;

namespace LargeBson
{
    public class DeserializedBson : IDisposable
    {
        public object Data { get; }
        private readonly IDisposable _innerStream;

        internal DeserializedBson(object data, IDisposable innerStream)
        {
            Data = data;
            _innerStream = innerStream;
        }
        
        public void Dispose()
        {
            _innerStream.Dispose();
        }
    }
}