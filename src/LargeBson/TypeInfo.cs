using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LargeBson
{
    class TypePropertyList
    {
        public IReadOnlyList<PropertyInfo> Properties { get; }
        
        public TypePropertyList(Type t)
        {
            Properties = t.GetProperties().Select(p => new PropertyInfo(p)).ToList();
        }

        static ConcurrentDictionary<Type, TypePropertyList> Dic = new ConcurrentDictionary<Type, TypePropertyList>();
        public static TypePropertyList Get(Type t)
        {
            if (Dic.TryGetValue(t, out var rv))
                return rv;
            return Dic[t] = new TypePropertyList(t);
        }

        public PropertyInfo GetProperty(in ArraySegment<byte> name)
        {
            foreach (var p in Properties)
            {
                if (p.CstringName.Length == name.Count)
                {
                    var mismatch = false;
                    for (var c = 0; c < p.CstringName.Length; c++)
                    {
                        if (p.CstringName[c] != name[c])
                        {
                            mismatch = true;
                            break;
                        }
                    }

                    if (!mismatch)
                        return p;
                }
            }

            throw new ArgumentException("Property not found: " +
                                        Encoding.UTF8.GetString(
                                            name.Slice(0, name.Count - 1)));
        }
    }
}