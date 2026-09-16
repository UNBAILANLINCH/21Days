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
                return File.Exists(GetSlotPath(slot));
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
                await UniTask.RunOnThreadPool(() => WriteAtomic(path, json), cancellationToken: ct);
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

        public async UniTask<bool> LoadAsync(int slot, CancellationToken ct = default)
        {
            string path = GetSlotPath(slot);
            string json;
            long startMs = NowMs;

            try
            {
                json = await UniTask.RunOnThreadPool(
                    () => File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null,
                    cancellationToken: ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Log.Error($"存档槽 {slot} 读盘失败：{path}，{e}");
                TrackSaveFailed(TelemetryKeys.SaveEvents.Load, slot, e, "read_failed");
                return false;
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
                return false;
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
                return false;
            }

            if (envelope == null || envelope.Partitions == null)
            {
                Log.Error($"存档槽 {slot} 的内容不是本工程的存档格式（缺 partitions）：{path}");
                TrackSaveFailed(TelemetryKeys.SaveEvents.Corrupt, slot, "存档缺 partitions，不是本工程的格式", "no_partitions");
                return false;
            }

            if (envelope.FormatVersion > CurrentFormatVersion)
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
                return false;
            }

            // 先全部读进临时字典，全部成功了再整体替换，避免出错时留下半份存档。
            Dictionary<Type, ISaveData> loaded = new Dictionary<Type, ISaveData>(envelope.Partitions.Count);
            bool anyPartitionCorrupt = false;

            foreach (KeyValuePair<string, SavePartition> pair in envelope.Partitions)
            {
                if (pair.Value == null || pair.Value.Data == null)
                {
                    Log.Warn($"存档槽 {slot} 的分区 {pair.Key} 是空的，跳过");
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
                    Log.Warn($"分区 {pair.Key} 在存档里是版本 {pair.Value.Version}，比代码里的 {data.Version} 新，"
                             + "按原样读入，字段对不上的会退回默认值");
                }

                loaded[type] = data;
            }

            if (anyPartitionCorrupt)
            {
                Log.Error($"存档槽 {slot} 存在损坏分区，内存里的存档保持不变");

                // 每个坏分区上面已经各埋了一条 corrupt，这里补的是「这次读档的最终结论是失败」——
                // 少了它，日志里只看得到「某个分区坏了」，看不出整次 LoadAsync 返回了 false。
                TrackSaveFailed(TelemetryKeys.SaveEvents.Load, slot, "存在损坏分区，读档放弃", "partition_corrupt");
                return false;
            }

            partitions.Clear();
            foreach (KeyValuePair<Type, ISaveData> pair in loaded)
            {
                partitions[pair.Key] = pair.Value;
            }

            Log.Info($"存档槽 {slot} 已读入：{partitions.Count} 个分区");
            telemetry.Track(
                TelemetryKeys.SaveEvents.Load,
                (TelemetryKeys.Props.Slot, slot),
                (TelemetryKeys.Props.Ms, NowMs - startMs),
                (TelemetryKeys.Props.N, partitions.Count));
            return true;
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
            return Path.Combine(SaveRoot, $"slot{slot}.json");
        }

        /// <summary>
        /// 原子写：内容先落到 .tmp，再整体替换正档。写入确认成功后才清掉 .tmp；
        /// 没成功时 .tmp 会保留下来（可能是手工恢复用的现场），不主动删除正档。
        /// 跑在线程池上，不要在这里碰任何 Unity API。
        /// </summary>
        private static void WriteAtomic(string path, string json)
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
                        File.Replace(temp, path, null);
                    }
                    catch (Exception)
                    {
                        // 退路：某些平台（部分 Android 外部存储、网络盘）不支持 Replace。
                        // 这一步不是原子的，但全程不主动删除正档：用 Copy 覆盖而不是
                        // 先 Delete 正档再 Move temp——Delete 之后 Move 万一再炸，
                        // 正档和 temp 会同时丢；Copy 中途失败正档还在、temp 也还在，不会两份都没了。
                        File.Copy(temp, path, overwrite: true);
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
