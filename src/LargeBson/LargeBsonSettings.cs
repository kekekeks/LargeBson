using System;

namespace LargeBson
{
    public class LargeBsonSettings
    {
        public delegate string PropertyNameResolverDelegate(System.Reflection.PropertyInfo prop);
        public PropertyNameResolverDelegate PropertyNameResolver { get; set; }
    }
}