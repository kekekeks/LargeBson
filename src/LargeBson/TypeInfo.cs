using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LargeBson
{
    class TypeInformation
    {
        public IReadOnlyList<PropertyInfo> Properties { get; }
        public bool IsArray { get; }
        public bool ImplementsIList { get; }
        public bool IsIList { get; }
        public Type ElementType { get; }
        public Type ListOfElementType { get; }
        public bool IsBsonArray { get; }
        private IToArrayConverter _toArrayConverter;
        public Type Type { get; }
        public TypeInformation(Type t)
        {
            Type = t;
            Properties = new PropertyInfo[0];
            if (t.IsArray)
            {
                IsBsonArray = true;
                if (t.GetArrayRank() != 1)
                    throw new NotSupportedException("Multidimensional arrays are not supported");
                IsArray = true;
                ElementType = t.GetElementType();
                _toArrayConverter =
                    (IToArrayConverter) Activator.CreateInstance(
                        typeof(ToArrayConverter<>).MakeGenericType(ElementType));
                ListOfElementType = typeof(List<>).MakeGenericType(ElementType);
            }
            else if (t.IsConstructedGenericType && t.GetGenericTypeDefinition() == typeof(IList<>))
            {
                IsBsonArray = true;
                IsIList = true;
                var el = t.GetGenericArguments()[0];
                ElementType = el;
                ListOfElementType = typeof(List<>).MakeGenericType(el);
            }
            else
            {
                var ilist = t.GetInterfaces().FirstOrDefault(i =>
                    i.IsConstructedGenericType && i.GetGenericTypeDefinition() == typeof(IList<>));
                if (ilist != null)
                {
                    IsBsonArray = true;
                    ImplementsIList = true;
                    var el = ilist.GetGenericArguments()[0];
                    ElementType = el;
                    ListOfElementType = t;
                }
                else
                    Properties = t.GetProperties().Select(p => new PropertyInfo(p)).ToList();
            }
        }

        static ConcurrentDictionary<Type, TypeInformation> Dic = new ConcurrentDictionary<Type, TypeInformation>();
        public static TypeInformation Get(Type t)
        {
            if (Dic.TryGetValue(t, out var rv))
                return rv;
            return Dic[t] = new TypeInformation(t);
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

        public WriteAdapter CreateWriteAdapter()
        {
            if (ListOfElementType != null)
                return new WriteAdapter(this, (IList) Activator.CreateInstance(ListOfElementType), _toArrayConverter);
            return new WriteAdapter(this, Activator.CreateInstance(Type));
        }
    }

    interface IToArrayConverter
    {
        object Convert(IList lst);
    }

    class ToArrayConverter<T> : IToArrayConverter
    {
        public object Convert(IList lst) => ((IList<T>) lst).ToArray();
    }

    struct WriteAdapter
    {
        private IList _list;
        private readonly IToArrayConverter _toArray;
        private readonly TypeInformation _info;
        private object _instance;
        private PropertyInfo _property;

        public WriteAdapter(TypeInformation info, object instance) : this()
        {
            _info = info;
            _instance = instance;
        }

        public WriteAdapter(TypeInformation info, IList list, IToArrayConverter toArray) : this()
        {
            _info = info;
            _list = list;
            _toArray = toArray;
        }

        public void SelectProperty(ArraySegment<byte> name)
        {
            if (_instance != null)
                _property = _info.GetProperty(name);
        }

        public Type CurrentPropertyType
        {
            get
            {
                if (_instance != null)
                    return _property.Type;
                return _info.ElementType;
            }
        }

        public void WriteValue(object value)
        {
            if (_instance != null)
                _property.Set(_instance, value);
            else
                _list.Add(value);
        }
        
        public object CreateInstance()
        {
            if (_instance != null)
                return _instance;
            if (_toArray == null)
                return _list;

            return _toArray.Convert(_list);
        }
    }
}