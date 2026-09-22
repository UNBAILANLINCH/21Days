// 职责：候选分区与提交前校验容器；Get<T> 会创建默认分区，不能复用于判断候选档是否缺失。
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Game.Core.Save
{
    public sealed class SaveSnapshot
    {
        private readonly Dictionary<Type, ISaveData> partitions = new Dictionary<Type, ISaveData>();
        internal SaveSnapshot(IEnumerable<KeyValuePair<Type, ISaveData>> source)
        {
            foreach (KeyValuePair<Type, ISaveData> pair in source) partitions.Add(pair.Key, Clone(pair.Value, pair.Key));
        }
        public T Require<T>() where T : class, ISaveData
        {
            if (!partitions.TryGetValue(typeof(T), out ISaveData value))
                throw new InvalidOperationException("存档缺少必要分区：" + typeof(T).Name);
            return (T)Clone(value, typeof(T));
        }
        public bool Contains<T>() where T : ISaveData => partitions.ContainsKey(typeof(T));
        public void Set<T>(T data) where T : class, ISaveData
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            partitions[typeof(T)] = Clone(data, typeof(T));
        }
        internal Dictionary<Type, ISaveData> CopyPartitions()
        {
            var copy = new Dictionary<Type, ISaveData>();
            foreach (KeyValuePair<Type, ISaveData> pair in partitions) copy.Add(pair.Key, Clone(pair.Value, pair.Key));
            return copy;
        }
        private static ISaveData Clone(ISaveData value, Type type) =>
            (ISaveData)JsonConvert.DeserializeObject(JsonConvert.SerializeObject(value), type);
    }
}
