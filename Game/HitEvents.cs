using System;
using System.Collections.Generic;

namespace SimPhi.Game
{
    /// <summary>
    /// 照抄 src/components/HitEvents.ts（原版 extends Array&lt;T&gt;）。
    /// update = 反向遍历，predicate 为真则移除；animate = 正向遍历回调。
    /// </summary>
    public sealed class HitEvents<T> : List<T>
    {
        public readonly Action Update;
        public readonly Action Animate;

        public HitEvents(Func<T, bool> updateCallback = null, Action<T> iterateCallback = null)
        {
            Func<T, bool> upd = updateCallback ?? (_ => false);
            Action<T> itr = iterateCallback ?? (_ => { });
            Update = () => Defilter(upd);
            Animate = () => Iterate(itr);
        }

        private void Defilter(Func<T, bool> predicate)
        {
            int i = Count;
            while (i-- > 0) if (predicate(this[i])) RemoveAt(i);
        }

        private void Iterate(Action<T> callback)
        {
            for (int i = 0; i < Count; i++) callback(this[i]);
        }

        public void Add2(T value) { Add(value); }   // 原版 add()；List 已有 Add，这里留个别名

        public void Clear2() { Clear(); }
    }
}
