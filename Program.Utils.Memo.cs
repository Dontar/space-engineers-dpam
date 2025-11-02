using System;
using System.Collections.Generic;

namespace IngameScript
{
    partial class Program
    {
        static class Memo
        {
            class CacheValue
            {
                public object Value;
                public long Age;
                public int DepHash;
                public TimeSpan CreatedAt;
                bool Time;

                public CacheValue(int depHash, object value, int age = 0) {
                    CreatedAt = Now;
                    DepHash = depHash;
                    Value = value;
                    Age = age;
                    Time = false;
                }
                public CacheValue(int depHash, object value, TimeSpan age) {
                    CreatedAt = Now;
                    DepHash = depHash;
                    Value = value;
                    Age = age.Ticks;
                    Time = true;
                }

                public bool Decay() {
                    if (Time) {
                        if (Now - CreatedAt >= TimeSpan.FromTicks(Age))
                            return false;
                        return true;
                    }
                    if (Age-- > 0)
                        return true;
                    return false;
                }
            }

            static readonly Dictionary<string, CacheValue> _dependencyCache = new Dictionary<string, CacheValue>();
            static readonly Queue<string> _cacheOrder = new Queue<string>();
            const int MaxCacheSize = 1000;

            static int GetDepHash(object dep) {
                if (dep is int)
                    return (int)dep;
                if (dep is object[]) {
                    var arr = (object[])dep;
                    unchecked {
                        int hash = 17;
                        foreach (var d in arr)
                            hash = hash * 31 + (d?.GetHashCode() ?? 0);
                        return hash;
                    }
                }
                return dep?.GetHashCode() ?? 0;
            }

            static object IntOf(Func<object, object> f, string context, object dep) {
                if (_dependencyCache.Count > MaxCacheSize) {
                    EvictOldestCacheItem();
                }

                int depHash = GetDepHash(dep);
                string cacheKey = context;// + ":" + depHash;

                CacheValue value;
                if (_dependencyCache.TryGetValue(cacheKey, out value)) {
                    bool isNotStale = dep is int ? value.Decay() : value.DepHash == depHash;
                    if (isNotStale)
                        return value.Value;
                }

                var result = f(value?.Value);
                if (dep is int)
                    _dependencyCache[cacheKey] = new CacheValue(depHash, result, (int)dep);
                else if (dep is TimeSpan)
                    _dependencyCache[cacheKey] = new CacheValue(depHash, result, (TimeSpan)dep);
                else
                    _dependencyCache[cacheKey] = new CacheValue(depHash, result, 0);
                _cacheOrder.Enqueue(cacheKey);
                return result;
            }

            static void EvictOldestCacheItem() {
                if (_cacheOrder.Count > 0) {
                    var oldestKey = _cacheOrder.Dequeue();
                    _dependencyCache.Remove(oldestKey);
                }
            }

            public static R Of<R, T>(string context, T dep, Func<T, R> f) => (R)IntOf(d => f(d != null ? (T)d : default(T)), context, dep);
            public static R Of<R>(string context, object dep, Func<R> f) => (R)IntOf(_ => f(), context, dep);

            public static void Of<T>(string context, T dep, Action<T> f) => IntOf(d => { f(d != null ? (T)d : default(T)); return null; }, context, dep);
            public static void Of(string context, object dep, Action f) => IntOf(_ => { f(); return null; }, context, dep);

            static TimeSpan Now;
            public static void Tick(TimeSpan timeSpan) {
                Now += timeSpan;
            }
        }
    }
}
