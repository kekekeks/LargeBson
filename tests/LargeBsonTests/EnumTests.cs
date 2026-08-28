using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LargeBson;
using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
using Xunit;

namespace LargeBsonTests
{
    public class EnumTests
    {
        public enum ByteEnum : byte
        {
            Zero = 0,
            Max = 255
        }

        public enum IntEnum
        {
            Zero = 0,
            One = 1,
            Negative = -5,
            Max = int.MaxValue
        }

        public enum UIntEnum : uint
        {
            Zero = 0,
            Big = uint.MaxValue
        }

        public enum LongEnum : long
        {
            Zero = 0,
            Min = long.MinValue
        }

        public enum ULongEnum : ulong
        {
            Zero = 0,
            Big = ulong.MaxValue
        }

        public class Model
        {
            public IntEnum Int { get; set; }
            public ByteEnum Byte { get; set; }
            public UIntEnum UInt { get; set; }
            public LongEnum Long { get; set; }
            public ULongEnum ULong { get; set; }
            public IntEnum? NullableSet { get; set; }
            public IntEnum? NullableUnset { get; set; }
            public List<IntEnum> List { get; set; }
            public IntEnum[] Array { get; set; }
            public Dictionary<string, IntEnum> ValueDic { get; set; }
            public Dictionary<IntEnum, string> KeyDic { get; set; }
            public Nested Sub { get; set; }
        }

        public class Nested
        {
            public IntEnum Int { get; set; }
        }

        public class IntModel
        {
            public int Int { get; set; }
        }

        public class EnumModel
        {
            public IntEnum Int { get; set; }
        }

        static byte[] Serialize(object o)
        {
            var ms = new MemoryStream();
            using (var s = new LargeBsonSerializer().Serialize(o))
                s.CopyTo(ms);
            return ms.ToArray();
        }

        static T RoundTrip<T>(T o)
        {
            using (var deserialized =
                new LargeBsonSerializer().Deserialize(new MemoryStream(Serialize(o)), typeof(T)).Result)
                return (T) deserialized.Data;
        }

        [Fact]
        public void EnumsAreSerializedAsTheirUnderlyingValue()
        {
            Assert.Equal(Serialize(new IntModel {Int = 1}), Serialize(new EnumModel {Int = IntEnum.One}));
        }

        [Fact]
        public void EnumsAreCompatibleWithJsonNet()
        {
            var ms = new MemoryStream();
            using (var bsonWriter = new BsonWriter(ms))
                new JsonSerializer().Serialize(bsonWriter, new EnumModel {Int = IntEnum.One});

            Assert.Equal(ms.ToArray(), Serialize(new EnumModel {Int = IntEnum.One}));
        }

        [Fact]
        public void EnumsCanBeDeserializedFromJsonNet()
        {
            var ms = new MemoryStream();
            using (var bsonWriter = new BsonWriter(ms))
                new JsonSerializer().Serialize(bsonWriter, new IntModel {Int = (int) IntEnum.Negative});

            using (var deserialized =
                new LargeBsonSerializer().Deserialize(new MemoryStream(ms.ToArray()), typeof(EnumModel)).Result)
                Assert.Equal(IntEnum.Negative, ((EnumModel) deserialized.Data).Int);
        }

        [Fact]
        public void EnumsSurviveRoundTrip()
        {
            var model = new Model
            {
                Int = IntEnum.Negative,
                Byte = ByteEnum.Max,
                UInt = UIntEnum.Big,
                Long = LongEnum.Min,
                ULong = ULongEnum.Big,
                NullableSet = IntEnum.Max,
                List = new List<IntEnum> {IntEnum.One, IntEnum.Negative},
                Array = new[] {IntEnum.Max, IntEnum.Zero},
                ValueDic = new Dictionary<string, IntEnum> {["foo"] = IntEnum.Negative},
                KeyDic = new Dictionary<IntEnum, string> {[IntEnum.One] = "bar"},
                Sub = new Nested {Int = IntEnum.Max}
            };

            var rt = RoundTrip(model);

            Assert.Equal(IntEnum.Negative, rt.Int);
            Assert.Equal(ByteEnum.Max, rt.Byte);
            Assert.Equal(UIntEnum.Big, rt.UInt);
            Assert.Equal(LongEnum.Min, rt.Long);
            Assert.Equal(ULongEnum.Big, rt.ULong);
            Assert.Equal(IntEnum.Max, rt.NullableSet);
            Assert.Null(rt.NullableUnset);
            Assert.Equal(new[] {IntEnum.One, IntEnum.Negative}, rt.List);
            Assert.Equal(new[] {IntEnum.Max, IntEnum.Zero}, rt.Array);
            Assert.Equal(IntEnum.Negative, rt.ValueDic["foo"]);
            Assert.Equal("bar", rt.KeyDic[IntEnum.One]);
            Assert.Equal(IntEnum.Max, rt.Sub.Int);
        }

        [Fact]
        public void EnumCantBeSerializedAsRootObject()
        {
            Assert.Throws<InvalidOperationException>(() => Serialize(IntEnum.One));
        }
    }
}
