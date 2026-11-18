// .NET Framework 4.8 (AutoCAD 2024 and older) lacks a few APIs the engine uses. Compiled only for net48.
#if NETFRAMEWORK
namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }

namespace PVLocal
{
    internal static class Net48
    {
        public static T? MinBy<T, K>(this IEnumerable<T> src, Func<T, K> key) where K : IComparable<K>
        {
            T? best = default; K? bk = default; bool any = false;
            foreach (var x in src) { var k = key(x); if (!any || k.CompareTo(bk!) < 0) { best = x; bk = k; any = true; } }
            return best;
        }
        public static IEnumerable<(A First, B Second)> Zip<A, B>(this IEnumerable<A> a, IEnumerable<B> b) => a.Zip(b, (x, y) => (x, y));
        public static V? GetValueOrDefault<K, V>(this IDictionary<K, V> d, K k) => d.TryGetValue(k, out var v) ? v : default;
        public static void Deconstruct<K, V>(this KeyValuePair<K, V> kv, out K k, out V v) { k = kv.Key; v = kv.Value; }
    }
}
#endif
