using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace LargeBson
{
    class PropertyInfo
    {
        private Func<object, object> _getter;
        private Action<object, object> _setter;
        public string Name { get; }
        public byte[] CstringName { get; }
        public PropertyInfo(System.Reflection.PropertyInfo prop, LargeBsonSettings settings)
        {
            Name = settings?.PropertyNameResolver?.Invoke(prop) ?? prop.Name;
            CstringName = Encoding.UTF8.GetBytes(Name).Concat(new byte[] {0}).ToArray();
            
            var obj = Expression.Parameter(typeof(object), "obj");
            var propexpr = Expression.Property(Expression.Convert(obj, prop.DeclaringType), prop);

            Type = prop.PropertyType;
            
            if (prop.CanRead)
                _getter = Expression.Lambda<Func<object, object>>(Expression.Convert(propexpr, typeof(object)), obj).Compile();

            if (prop.CanWrite)
            {
                if (prop.SetMethod.GetParameters().Length == 1)
                {
                    var value = Expression.Parameter(typeof(object), "value");
                    _setter = Expression.Lambda<Action<object, object>>(
                        Expression.Assign(propexpr, Expression.Convert(value, prop.PropertyType)),
                        obj, value).Compile();
                }
            }
        }

        public void Set(object target, object value) => _setter(target, value);
        public object Get(object target) => _getter(target);
        public bool CanRead => _getter != null;
        public bool CanWrite => _setter != null;
        public Type Type { get; }
    }
}