using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace LargeBson
{
    internal static class Compat
    {
#if !NETCOREAPP
        public static ArraySegment<T> Slice<T>(this ArraySegment<T> seg, int index)
        {
            if (seg.Count == 0)
                throw new InvalidOperationException();

            if ((uint)index > (uint)seg.Count)
            {
                throw new IndexOutOfRangeException();
            }

            return new ArraySegment<T>(seg.Array, seg.Offset + index, seg.Count - index);
        }

        public static ArraySegment<T> Slice<T>(this ArraySegment<T> seg, int index, int count)
        {
            if (seg.Count == 0)
                throw new InvalidOperationException();

            if ((uint)index > (uint)seg.Count || (uint)count > (uint)(seg.Count - index))
            {
                throw new IndexOutOfRangeException();
            }

            return new ArraySegment<T>(seg.Array, seg.Offset + index, count);
        }

        public static unsafe string GetString(this Encoding encoding, ArraySegment<byte> segment)
        {
            fixed (byte* p = segment.Array)
                return encoding.GetString(p + segment.Offset, segment.Count);
        }
        
        public static unsafe string GetString(this Encoding encoding, Span<byte> span)
        {
            fixed (byte* p = span)
                return encoding.GetString(p, span.Length);
        }

        public static unsafe int GetBytes(this Encoding encoding, string chars, Span<byte> bytes)
            => encoding.GetBytes(chars.AsSpan(), bytes);
        
        public static unsafe int GetBytes(this Encoding encoding, ReadOnlySpan<char> chars, Span<byte> bytes)
        {
            fixed (char* charsPtr = chars)
            fixed (byte* bytesPtr = bytes)
            {
                return encoding.GetBytes(charsPtr, chars.Length, bytesPtr, bytes.Length);
            }
        }

        static async ValueTask<int> ReadWithPool(this Stream s, Memory<byte> buffer)
        {
            var rented = ArrayPool<byte>.Shared.Rent(1024);
            try
            {
                var read = await s.ReadAsync(rented, 0, Math.Min(buffer.Length, rented.Length));
                rented.CopyTo(buffer);
                return read;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
        
        public static unsafe ValueTask<int> ReadAsync(this Stream s, Memory<byte> buffer)
        {
            if (MemoryMarshal.TryGetArray<byte>(buffer, out var segment))
                return new ValueTask<int>(s.ReadAsync(segment.Array, segment.Offset, segment.Count));
            return ReadWithPool(s, buffer);
        }
        
        [ThreadStatic] private static byte[] _guidBuffer;

#endif

        public static Guid CreateGuid(Span<byte> data)
        {
#if NETCOREAPP
            return new Guid(data);
#else
            if(_guidBuffer == null)
                _guidBuffer = new byte[16];
            data.CopyTo(_guidBuffer);
            return new Guid(_guidBuffer);
#endif
        }
    }

}