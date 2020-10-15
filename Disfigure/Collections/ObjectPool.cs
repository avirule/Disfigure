#region

using System;
using System.Collections.Concurrent;

#endregion

namespace Disfigure.Collections
{
    public class ObjectPool<T>
    {
        private readonly ConcurrentBag<T> _Pool;
        private readonly Func<T> _ObjectFactory;

        public ObjectPool(Func<T> objectFactory)
        {
            _ObjectFactory = objectFactory;
            _Pool = new ConcurrentBag<T>();
        }

        public T Rent() => _Pool.TryTake(out T obj) ? obj ?? throw new NullReferenceException() : _ObjectFactory();

        public void Return(T obj) => _Pool.Add(obj ?? throw new NullReferenceException());
    }
}