// 职责：ISaveService 的 JSON 实现——信封格式、原子写、逐分区版本迁移、损坏容错。
// 为什么新建：ISaveService 是契约，实现分开放（architecture.md 第 7 节：换云存档只换实现）；
// 波 1 没有任何存档相关文件可复用或扩展。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Boot;
using Game.Core.Logging;
using Game.Core.Platform;
using Game.Core.Telemetry;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.Core.Save
{
    /// <summary>
    /// JSON 存档。文件落在 <c>&lt;IPlatformService.SaveRoot&gt;/slot&lt;N&gt;.json</c>，格式：
    /// <code>
    /// {
    ///   "formatVersion": 1,
    ///   "partitions": {
    ///     "Game.Core.Save.SettingsSaveData": { "version": 1, "data": { "MasterVolume": 1.0, ... } }
    ///   }
    /// }
    /// </code>
    /// 键是分区类型的全名，所以改命名空间/类名等于换了一个分区（老数据会被当成「代码里已经没有的分区」跳过）。
    /// <para>
    /// 写盘用「先写 .tmp 再原子替换」，中途断电只会留下一个 .tmp，正档不会半截；
    /// 磁盘 IO 放线程池，序列化留在主线程（分区对象是玩法在改的，扔到别的线程上序列化会撞上竞态）。
    /// 个别平台不支持 <see cref="File.Replace(string, string, string)"/> 时会退化成拷贝覆盖正档，
    /// 全程不会主动删除已存在的正档：拷贝失败会保留 .tmp 现场供手工恢复，不会出现正档和临时文件同时丢失。
    /// </para>
    /// </summary>
    public sealed class JsonSaveService : ISaveService, IGameService
    {
        /// <summary>存档信封的格式版本。改信封结构（不是分区内容）时才动它。</summary>
        public const int CurrentFormatVersion = 1;

        private const string TempSuffix = ".tmp";
        // ponytail: 全部存档 IO 共用锁；并行大档写入成为瓶颈时再改为按路径锁。
        private static readonly object DiskGate = new object();
        // ponytail: async gate also serializes full requests; use per-path queues if throughput matters.
        private static readonly SemaphoreSlim IoGate = new SemaphoreSlim(1, 1);

        private readonly IPlatformService platform;
        private readonly JsonSerializer serializer;
        private readonly JsonSerializerSettings serializerSettings;

        /// <summary>内存里的当前存档：分区类型 → 分区实例。</summary>
        private readonly Dictionary<Type, ISaveData> partitions = new Dictionary<Type, ISaveData>();

        /// <summary>分区类型全名 → 类型。反射扫程序集很贵，扫到的结果缓存下来。</summary>
        private readonly Dictionary<string, Type> typeCache = new Dictionary<string, Type>(StringComparer.Ordinal);

        private readonly ITelemetryScope telemetry;
        private readonly ITelemetryClock clock;

        /// <summary>
        /// 两个埋点参数允许为 null（EditMode 测试里直接 new 出来的存档服务没有容器）：
        /// 拿不到就整条埋点链路变空操作，读写存档的行为一个字节都不变。
        /// </summary>
        public JsonSaveService(IPlatformService platform, ITelemetryService telemetry, ITelemetryClock clock)
        {
            this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
            this.clock = clock;
            this.telemetry = telemetry == null
                ? (ITelemetryScope)NullTelemetryScope.Instance
                : telemetry.Scope(TelemetryKeys.Save);

            serializerSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,

                // 存档要能人读能 diff，不写 $type：类型信息由 partitions 的键给出，
                // 带 $type 反而会让「改了类名的老档」直接反序列化失败。
                TypeNameHandling = TypeNameHandling.None,

                // 老档里多出来的字段（代码里已经删掉的）直接忽略，不因此判定为损坏。
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Include,
            };
            serializer = JsonSerializer.Create(serializerSettings);
        }

        /// <summary>存档根目录。来自平台服务，不自己拼 persistentDataPath。</summary>
        public string SaveRoot => platform.SaveRoot;

        public UniTask InitializeAsync(CancellationToken ct)
        {
            try
            {
                Directory.CreateDirectory(SaveRoot);
            }
            catch (Exception e)
            {
                // 建不出目录不该把启动打断——存不了档比进不去游戏轻。真要存的时候会再报一次。
                Log.Error($"存档目录创建失败：{SaveRoot}，{e.Message}");
                telemetry.TrackError(
                    TelemetryKeys.SaveEvents.Write,
                    e,
                    TelemetryProps.Of((TelemetryKeys.Props.Reason, "save_root_unavailable")));
            }

            return UniTask.CompletedTask;
        }

        public T Get<T>() where T : class, ISaveData, new()
        {
            if (partitions.TryGetValue(typeof(T), out ISaveData existing))
            {
                return (T)existing;
            }

            T created = new T();
            partitions[typeof(T)] = created;
            return created;
        }

        public bool Exists(int slot)
        {
            try
            {
                lock (DiskGate) return File.Exists(GetSlotPath(slot));
            }
            catch (Exception e)
            {
                Log.Error($"检查存档槽 {slot} 是否存在时出错：{e.Message}");

                // 契约里 core.save 只有 write / load / corrupt / migrate 四个事件，
                // 所以这类「读这一侧出的问题」挂在 load 下面，用 reason 区分具体是哪条分支。
                TrackSaveFailed(TelemetryKeys.SaveEvents.Load, slot, e, "exists_check_failed");
                return false;
            }
        }

        public void Delete(int slot)
        {
            lock (DiskGate) DeleteLocked(slot);
        }

        private void DeleteLocked(int slot)
        {
            string path = GetSlotPath(slot);
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                // 上一次写盘崩在半路时可能留下残片，一并清掉。
                string temp = path + TempSuffix;
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch (Exception e)
            {
                Log.Error($"删除存档槽 {slot} 失败：{e.Message}");
                TrackSaveFailed(TelemetryKeys.SaveEvents.Write, slot, e, "delete_failed");
            }
        }

        public async UniTask<bool> SaveAsync(int slot, CancellationToken ct = default)
        {
            string path = GetSlotPath(slot);
            string json;
            long startMs = NowMs;

            // 序列化在调用线程（主线程）上做：分区对象随时可能被玩法改，丢到线程池上序列化就是竞态。
            try
            {
                SaveEnvelope envelope = new SaveEnvelope
                {
                    FormatVersion = CurrentFormatVersion,
                    Partitions = new Dictionary<string, SavePartition>(partitions.Count, StringComparer.Ordinal),
                };

                foreach (KeyValuePair<Type, ISaveData> pair in partitions)
                {
                    envelope.Partitions[pair.Key.FullName] = new SavePartition
                    {
                        Version = pair.Value.Version,
                        Data = JObject.FromObject(pair.Value, serializer),
                    };
                }

                json = JsonConvert.SerializeObject(envelope, serializerSettings);
            }
            catch (Exception e)
            {
                Log.Error($"存档槽 {slot} 序列化失败：{e}");
                TrackSaveFailed(TelemetryKeys.SaveEvents.Write, slot, e, "serialize_failed");
                return false;
            }

            try
            {
                await IoGate.WaitAsync(ct);
                try { await UniTask.RunOnThreadPool(() => WriteAtomic(path, json), cancellationToken: ct); }
                finally { IoGate.Release(); }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Log.Error($"存档槽 {slot} 写盘失败：{path}，{e}");
                TrackSaveFailed(TelemetryKeys.SaveEvents.Write, slot, e, "write_failed");
                return false;
            }

            Log.Info($"存档已写入槽 {slot}：{partitions.Count} 个分区");

            // bytes 按 UTF-8 的实际字节数算而不是 json.Length：中文一个字符占三字节，
            // 拿字符数当大小会让「存档为什么涨到 10 MB」这类问题从一开始就查错方向。
            telemetry.Track(
                TelemetryKeys.SaveEvents.Write,
                (TelemetryKeys.Props.Slot, slot),
                (TelemetryKeys.Props.Ms, NowMs - startMs),
                (TelemetryKeys.Props.Bytes, Encoding.UTF8.GetByteCount(json)));
            return true;
        }

        public async UniTask<SaveSnapshot> ReadCandidateAsync(int slot, CancellationToken ct = default)
        {
            // 候选里装的是已经迁移过的分区（SaveSnapshot 构造时再克隆一份隔离），Commit 不会再迁一次。
            Dictionary<Type, ISaveData> loaded = await ReadPartitionsAsync(slot, ct);
            return loaded == null ? null : new SaveSnapshot(loaded);
        }

        /// <summary>
        /// 读盘、解析、逐分区反序列化并按需迁移（每个旧版本分区只调一次 <see cref="ISaveData.Migrate"/>）。
        /// 失败返回 null，不碰内存里的当前存档。<see cref="ReadCandidateAsync"/> 与 <see cref="LoadAsync"/> 共用这一段。
        /// </summary>
        private async UniTask<Dictionary<Type, ISaveData>> ReadPartitionsAsync(int slot, CancellationToken ct)
        {
            string path = GetSlotPath(slot);
            string json;
            long startMs = NowMs;

            try
            {
                await IoGate.WaitAsync(ct);
                try { json = await UniTask.RunOnThreadPool(() => ReadLocked(path), cancellationToken: ct); }
                finally { IoGate.Release(); }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Log.Error($"存档槽 {slot} 读盘失败：{path}，{e}");
                TrackSaveFailed(TelemetryKeys.SaveEvents.Load, slot, e, "read_failed");
                return null;
            }

            if (json == null)
            {
                Log.Warn($"存档槽 {slot} 没有文件，按新档处理：{path}");

                // 没有文件是首次进游戏的正常路径，只记 W 不记 E；但它必须留痕——
                // 「玩家说存档没了」的第一件事就是确认当时到底有没有读到文件。
                telemetry.TrackWarn(
                    TelemetryKeys.SaveEvents.Load,
                    TelemetryProps.Of(
                        (TelemetryKeys.Props.Slot, slot),
                        (TelemetryKeys.Props.Reason, "no_file")));
                return null;
            }

            SaveEnvelope envelope;
            try
            {
                envelope = JsonConvert.DeserializeObject<SaveEnvelope>(json, serializerSettings);
            }
            catch (JsonException e)
            {
                Log.Error($"存档槽 {slot} 的 JSON 解析不了，文件已损坏：{path}，{e.Message}");
                TrackSaveFailed(TelemetryKeys.SaveEvents.Corrupt, slot, e, "json_unparsable");
                return null;
            }

            if (envelope == null || envelope.Partitions == null)
            {
                Log.Error($"存档槽 {slot} 的内容不是本工程的存档格式（缺 partitions）：{path}");
                TrackSaveFailed(TelemetryKeys.SaveEvents.Corrupt, slot, "存档缺 partitions，不是本工程的格式", "no_partitions");
                return null;
            }

            if (envelope.FormatVersion < 1 || envelope.FormatVersion > CurrentFormatVersion)
            {
                Log.Error($"存档槽 {slot} 的信封版本是 {envelope.FormatVersion}，本版本只认到 {CurrentFormatVersion}。"
                          + "多半是用更新的版本存过档，不要用旧版本覆盖它。");

                // 版本号写进属性：判定用到的数值必须留在日志里，否则只知道「版本太新」，
                // 不知道新到哪去了，也就没法判断玩家是从哪个版本回滚下来的。
                telemetry.TrackError(
                    TelemetryKeys.SaveEvents.Load,
                    $"存档信封版本 {envelope.FormatVersion} 比代码认得的 {CurrentFormatVersion} 新",
                    TelemetryProps.Of(
                        (TelemetryKeys.Props.Slot, slot),
                        (TelemetryKeys.Props.From, envelope.FormatVersion),
                        (TelemetryKeys.Props.To, CurrentFormatVersion),
                        (TelemetryKeys.Props.Reason, "format_too_new")));
                return null;
            }

            // 先全部读进临时字典，全部成功了再整体替换，避免出错时留下半份存档。
            Dictionary<Type, ISaveData> loaded = new Dictionary<Type, ISaveData>(envelope.Partitions.Count);
            bool anyPartitionCorrupt = false;

            foreach (KeyValuePair<string, SavePartition> pair in envelope.Partitions)
            {
                if (pair.Value == null || pair.Value.Data == null)
                {
                    Log.Warn($"存档槽 {slot} 的分区 {pair.Key} 是空的，跳过");
                    anyPartitionCorrupt = true;
                    continue;
                }

                Type type = ResolvePartitionType(pair.Key);
                if (type == null)
                {
                    // 代码里已经删掉的分区。存档不该因此判损坏——保留原样跳过即可。
                    Log.Warn($"存档槽 {slot} 里的分区 {pair.Key} 在当前代码里不存在，跳过");
                    continue;
                }

                ISaveData data;
                try
                {
                    // 只捕获「JSON 内容和代码类型对不上」这一类——这才是存档损坏；
                    // Migrate 抛出的异常单独处理，那是代码 bug，不能落进这个 catch 被当成损坏吞掉。
                    data = (ISaveData)pair.Value.Data.ToObject(type, serializer);
                }
                catch (Exception e) when (e is JsonException || e is FormatException || e is InvalidCastException)
                {
                    Log.Error($"存档槽 {slot} 的分区 {pair.Key} 反序列化失败，分区已损坏，跳过：{e.Message}");

                    // key 带上是哪个分区：一个档里坏一个分区和坏全部分区，处置方式完全不同。
                    telemetry.TrackError(
                        TelemetryKeys.SaveEvents.Corrupt,
                        e,
                        TelemetryProps.Of(
                            (TelemetryKeys.Props.Slot, slot),
                            (TelemetryKeys.Props.Key, pair.Key)));
                    anyPartitionCorrupt = true;
                    continue;
                }

                if (data == null)
                {
                    Log.Warn($"存档槽 {slot} 的分区 {pair.Key} 反序列化出 null，跳过");
                    anyPartitionCorrupt = true;
                    continue;
                }

                if (pair.Value.Version < data.Version)
                {
                    try
                    {
                        data.Migrate(pair.Value.Version);
                    }
                    catch (Exception e)
                    {
                        // Migrate 的实现本身抛异常是代码 bug，不是存档损坏——记下来后继续往上抛，
                        // 不能吞掉伪装成「返回 false」，否则这种 bug 永远暴露不出来。
                        Log.Error($"分区 {pair.Key} 的 Migrate({pair.Value.Version}) 实现抛出异常：{e}");
                        telemetry.TrackError(
                            TelemetryKeys.SaveEvents.Migrate,
                            e,
                            TelemetryProps.Of(
                                (TelemetryKeys.Props.Slot, slot),
                                (TelemetryKeys.Props.Key, pair.Key),
                                (TelemetryKeys.Props.From, pair.Value.Version),
                                (TelemetryKeys.Props.To, data.Version)));
                        throw;
                    }

                    Log.Info($"分区 {pair.Key} 已从版本 {pair.Value.Version} 迁移到 {data.Version}");

                    // 迁移是「转移」，所以两端用契约里的 from / to（这里装的是版本号而不是状态名）。
                    telemetry.Track(
                        TelemetryKeys.SaveEvents.Migrate,
                        (TelemetryKeys.Props.Slot, slot),
                        (TelemetryKeys.Props.Key, pair.Key),
                        (TelemetryKeys.Props.From, pair.Value.Version),
                        (TelemetryKeys.Props.To, data.Version));
                }
                else if (pair.Value.Version > data.Version)
                {
                    Log.Error($"分区 {pair.Key} 版本 {pair.Value.Version} 高于当前支持的 {data.Version}");
                    return null;
                }

                loaded[type] = data;
            }

            if (anyPartitionCorrupt)
            {
                Log.Error($"存档槽 {slot} 存在损坏分区，内存里的存档保持不变");

                // 每个坏分区上面已经各埋了一条 corrupt，这里补的是「这次读档的最终结论是失败」——
                // 少了它，日志里只看得到「某个分区坏了」，看不出整次 LoadAsync 返回了 false。
                TrackSaveFailed(TelemetryKeys.SaveEvents.Load, slot, "存在损坏分区，读档放弃", "partition_corrupt");
                return null;
            }

            telemetry.Track(TelemetryKeys.SaveEvents.Load, (TelemetryKeys.Props.Slot, slot), (TelemetryKeys.Props.N, loaded.Count));
            return loaded;
        }

        public async UniTask<bool> LoadAsync(int slot, CancellationToken ct = default)
        {
            Dictionary<Type, ISaveData> loaded = await ReadPartitionsAsync(slot, ct);
            if (loaded == null) return false;
            ct.ThrowIfCancellationRequested();

            // 直接换上刚迁移完的那批实例，不走 SaveSnapshot：快照为隔离会做 JSON 往返克隆，
            // 克隆只带得过「可读写的公开属性」，Migrate 里对其余状态的改动会在克隆里丢掉，
            // 读档后 Get<T>() 拿到的就不再是 Migrate 作用过的那个对象（d5a9d12 引入的回归）。
            partitions.Clear();
            foreach (KeyValuePair<Type, ISaveData> pair in loaded) partitions.Add(pair.Key, pair.Value);
            return true;
        }

        public SaveSnapshot Capture() => new SaveSnapshot(partitions);

        public void Commit(SaveSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            Dictionary<Type, ISaveData> copy = snapshot.CopyPartitions();
            partitions.Clear();
            foreach (KeyValuePair<Type, ISaveData> pair in copy) partitions.Add(pair.Key, pair.Value);
        }

        /// <summary>
        /// 清空内存分区，下次 <see cref="Get{T}"/> 建默认实例；磁盘不动。
        /// 与 <see cref="LoadAsync"/> 一样是整体替换：旧实例从此脱钩，所以任何服务都不得缓存分区实例（见接口注释）。
        /// 类型缓存（typeCache）保留：它只是「全名 → 类型」的反射结果，与存档内容无关。
        /// </summary>
        public void ResetAll()
        {
            partitions.Clear();
        }

        /// <summary>埋点层自己的时钟。拿不到时恒为 0（ms 记成 0），不影响任何业务路径。</summary>
        private long NowMs => clock == null ? 0L : clock.MillisecondsNow;

        /// <summary>
        /// 埋一条存档失败（带异常）。契约里 <c>core.save</c> 只有 write / load / corrupt / migrate 四个事件，
        /// 所以同一个事件下的多条失败分支靠 <c>reason</c> 区分，而不是去发明新事件名。
        /// </summary>
        private void TrackSaveFailed(string evt, int slot, Exception error, string reason)
        {
            telemetry.TrackError(
                evt,
                error,
                TelemetryProps.Of(
                    (TelemetryKeys.Props.Slot, slot),
                    (TelemetryKeys.Props.Reason, reason)));
        }

        /// <summary>埋一条存档失败（只有一句话，没有异常）。</summary>
        private void TrackSaveFailed(string evt, int slot, string message, string reason)
        {
            telemetry.TrackError(
                evt,
                message,
                TelemetryProps.Of(
                    (TelemetryKeys.Props.Slot, slot),
                    (TelemetryKeys.Props.Reason, reason)));
        }

        /// <summary>槽位文件的完整路径。</summary>
        public string GetSlotPath(int slot)
        {
            if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
            return Path.Combine(SaveRoot, $"slot{slot}.json");
        }

        private static string ReadLocked(string path)
        {
            lock (DiskGate) return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }

        public async UniTask<T> ReadProfileAsync<T>(string name, CancellationToken ct = default) where T : class, new()
            => await ReadProfileAsync<T>(name, null, ct);

        public async UniTask<T> ReadProfileAsync<T>(string name, Action<T> validate, CancellationToken ct = default) where T : class, new()
        {
            string path = ProfilePath(name);
            string json;
            await IoGate.WaitAsync(ct);
            try { json = await UniTask.RunOnThreadPool(() => ReadLocked(path), cancellationToken: ct); }
            finally { IoGate.Release(); }
            if (json == null) return new T();
            try
            {
                T value;
                if (typeof(ISaveData).IsAssignableFrom(typeof(T)))
                {
                    // ISaveData 档案走版本信封，与槽位分区同一套迁移规则；高版本返回 null → 用默认值且不动文件。
                    value = DeserializeVersionedProfile<T>(name, json);
                    if (value == null) return new T();
                }
                else
                {
                    value = JsonConvert.DeserializeObject<T>(json, serializerSettings);
                }

                if (value == null) throw new JsonSerializationException("玩家档案为空");
                validate?.Invoke(value);
                return value;
            }
            catch (JsonException e)
            {
                await IoGate.WaitAsync(ct);
                try { await UniTask.RunOnThreadPool(() =>
                {
                    lock (DiskGate)
                    {
                        // 保留损坏现场。若并发写入已替换原内容，不移动新的有效档案。
                        if (File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == json)
                            File.Move(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
                    }
                }, cancellationToken: ct); }
                finally { IoGate.Release(); }
                telemetry.TrackError("profile_corrupt", e);
                return new T();
            }
        }

        public async UniTask WriteProfileAsync<T>(string name, T data, CancellationToken ct = default) where T : class
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            string path = ProfilePath(name);
            // ISaveData 档案写成带版本的信封 { "version": N, "data": {...} }，读回时才能按 Migrate 迁移；其余类型保持裸对象。
            string json = data is ISaveData versioned
                ? JsonConvert.SerializeObject(
                    new SavePartition { Version = versioned.Version, Data = JObject.FromObject(data, serializer) },
                    serializerSettings)
                : JsonConvert.SerializeObject(data, serializerSettings);
            await IoGate.WaitAsync(ct);
            try { await UniTask.RunOnThreadPool(() => WriteAtomic(path, json), cancellationToken: ct); }
            finally { IoGate.Release(); }
        }

        /// <summary>
        /// 解析 ISaveData 档案：有 <c>version</c> + <c>data</c> 两个键的是信封；没有的是版本信封之前写出的裸对象，按版本 1 处理。
        /// 存的版本低于代码版本 → 调一次 <see cref="ISaveData.Migrate"/>（抛异常是代码 bug，照常往上抛）；
        /// 高于代码版本 → 记 Error 返回 null，与槽位分区「高版本拒绝读取」一致，文件原样保留（不当损坏挪走）。
        /// JSON 结构不对抛 JsonException，由调用方按损坏处理。
        /// </summary>
        private T DeserializeVersionedProfile<T>(string name, string json) where T : class, new()
        {
            JObject root = JObject.Parse(json);
            int storedVersion = 1;
            JToken dataToken = root;
            if (root.TryGetValue("version", StringComparison.Ordinal, out JToken versionToken)
                && versionToken.Type == JTokenType.Integer
                && root.TryGetValue("data", StringComparison.Ordinal, out JToken envelopeData)
                && envelopeData.Type == JTokenType.Object)
            {
                storedVersion = versionToken.Value<int>();
                dataToken = envelopeData;
            }

            T value = dataToken.ToObject<T>(serializer);
            if (value == null) throw new JsonSerializationException("玩家档案为空");

            ISaveData data = (ISaveData)value;
            if (storedVersion > data.Version)
            {
                Log.Error($"玩家档案 {name} 版本 {storedVersion} 高于当前支持的 {data.Version}，拒绝读取，改用默认值");
                return null;
            }

            if (storedVersion < data.Version)
            {
                data.Migrate(storedVersion);
                Log.Info($"玩家档案 {name} 已从版本 {storedVersion} 迁移到 {data.Version}");
            }

            return value;
        }

        private string ProfilePath(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 64) throw new ArgumentException("档案名称非法", nameof(name));
            foreach (char character in name)
                if (!((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z') ||
                    (character >= '0' && character <= '9') || character == '-' || character == '_'))
                    throw new ArgumentException("档案名称非法", nameof(name));
            return Path.Combine(SaveRoot, "profile-" + name + ".json");
        }

        /// <summary>
        /// 原子写：内容先落到 .tmp，再整体替换正档。写入确认成功后才清掉 .tmp；
        /// 没成功时 .tmp 会保留下来（可能是手工恢复用的现场），不主动删除正档。
        /// 跑在线程池上，不要在这里碰任何 Unity API。
        /// </summary>
        private static void WriteAtomic(string path, string json)
        {
            lock (DiskGate) WriteLocked(path, json);
        }

        private static void WriteLocked(string path, string json)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temp = path + TempSuffix;
            bool committed = false;
            try
            {
                // 不带 BOM：存档给程序读，BOM 只会给别的解析器添麻烦。
                File.WriteAllText(temp, json, new UTF8Encoding(false));

                if (File.Exists(path))
                {
                    try
                    {
                        // File.Replace 是真正的原子替换，但只在同卷的常规文件系统上可用。
                        File.Replace(temp, path, path + ".bak");
                    }
                    catch (Exception)
                    {
                        // 退路：某些平台（部分 Android 外部存储、网络盘）不支持 Replace。
                        // 这一步不是原子的，但全程不主动删除正档：用 Copy 覆盖而不是
                        // 先 Delete 正档再 Move temp——Delete 之后 Move 万一再炸，
                        // 正档和 temp 会同时丢；Copy 中途失败正档还在、temp 也还在，不会两份都没了。
                        File.Copy(path, path + ".bak", overwrite: true);
                        try
                        {
                            File.Copy(temp, path, overwrite: true);
                        }
                        catch
                        {
                            // 失败时尝试恢复旧档；恢复失败也保留 .bak 与 .tmp 供后续恢复。
                            File.Copy(path + ".bak", path, overwrite: true);
                            throw;
                        }
                    }
                }
                else
                {
                    File.Move(temp, path);
                }

                committed = true;
            }
            catch (Exception)
            {
                if (!committed && File.Exists(temp))
                {
                    Log.Error($"存档写入失败，临时文件保留在 {temp}，可手工恢复");
                }

                throw;
            }
            finally
            {
                if (committed && File.Exists(temp))
                {
                    try
                    {
                        File.Delete(temp);
                    }
                    catch (Exception)
                    {
                        // 清残片失败不影响这次写盘的成败判定，下次 Delete(slot) 还会再清一遍。
                    }
                }
            }
        }

        /// <summary>
        /// 按类型全名找分区类型。存档里存的是不带程序集名的全名（可读、可 diff），
        /// 所以只能在已加载的程序集里扫；结果缓存，找不到也缓存（避免每次读档都重扫一遍）。
        /// </summary>
        private Type ResolvePartitionType(string fullName)
        {
            if (typeCache.TryGetValue(fullName, out Type cached))
            {
                return cached;
            }

            Type found = null;

            // 先在已经 Get<T>() 过的分区里找，绝大多数情况一次命中，不用碰反射。
            foreach (Type known in partitions.Keys)
            {
                if (string.Equals(known.FullName, fullName, StringComparison.Ordinal))
                {
                    found = known;
                    break;
                }
            }

            if (found == null)
            {
                foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type candidate;
                    try
                    {
                        candidate = assembly.GetType(fullName, false);
                    }
                    catch (Exception)
                    {
                        // 个别动态程序集查类型会抛，跳过就好。
                        continue;
                    }

                    if (candidate != null && typeof(ISaveData).IsAssignableFrom(candidate))
                    {
                        found = candidate;
                        break;
                    }
                }
            }

            typeCache[fullName] = found;
            return found;
        }

        /// <summary>存档文件的最外层结构。</summary>
        private sealed class SaveEnvelope
        {
            [JsonProperty("formatVersion")]
            public int FormatVersion { get; set; }

            /// <summary>键是分区类型全名。</summary>
            [JsonProperty("partitions")]
            public Dictionary<string, SavePartition> Partitions { get; set; }
        }

        /// <summary>一个分区在存档里的样子：版本号 + 原始 JSON 对象（延迟到知道类型后再转）。</summary>
        private sealed class SavePartition
        {
            [JsonProperty("version")]
            public int Version { get; set; }

            [JsonProperty("data")]
            public JObject Data { get; set; }
        }
    }
}
