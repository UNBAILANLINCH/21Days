// 职责：三渲二转换的账本，两张表：
//   ① 配对表：「源材质 → Toon 材质」，供 <see cref="ToonMaterialConverter"/> 查账与排查，**每次转换从零重建**；
//   ② 调色表：每份源材质的临时调色旋钮值（色相 / 饱和度 / 明度），**人写的权威数据，转换从不清它**。
// 为什么新建（project-root.md「加能力的顺序」）：
//   1. 复用不行：工程里现有的 ScriptableObject 都是玩法配置（*Config），没有一个存「资产替换账本」。
//   2. 扩展不行：塞进任何一个 Config 都名实不符——这是美术工序的中间产物，与玩法无关。
//   调色表放进这份资产而不另建：它和配对表用同一把钥匙（源材质的 GlobalObjectId），也同样只给转换器用。
//
// **配对表不是来源的权威载体，生成材质上的标签才是**（`ToonSource:<id>`，见转换器的 SourceGuidLabelPrefix）。
// 配对表只是同一份信息的可读索引，方便在 Inspector 里一眼看全；两者不一致时以标签为准。
//
// **调色表是权威载体**：材质上的 _GradeHueShift / _GradeSaturation / _GradeValue 每次转换都由这里写回。
// 所以它必须扛得住转换器的两个菜单——
//   · 「环境转 Toon 材质」开头只清配对表（ClearPairs），不碰调色表；
//   · 「删掉重新生成」只删 `t:Material`，这份资产是 ScriptableObject，不在删除范围内。
// 这份资产落在生成目录（Art/Materials/Toon/）里，但**不是生成物**：别整目录删，删了调色值就没了。
// 调色是美术新贴图到位前的临时方案（见 ToonLit.shader 里旋钮的注释），新贴图到了就把对应条目删掉或归零。
//
// 为什么当初要表、后来为什么来源换成标签（踩过的坑，别删）：
//   转换是把场景里渲染器的材质槽从「源材质」换成生成的 Toon 材质。删掉 Toon 材质资产重跑时，
//   槽已经指向被删的资产，源材质就无从得知——实测这么干过一次，glb 那批槽全变成 None，
//   只能把场景从 git 重新加载才救回来。
//   于是先做了这张表；后来又实测到表本身会被一次空跑清空（表是文件，会被误删、写坏、被逻辑清掉），
//   而标签长在材质资产自己身上，跟着资产走。所以来源改由标签承载。
//
// 存的是 id 字符串而不是对象引用：表与材质资产互相引用会形成环路，字符串没这个问题，
// 而且材质改名 / 挪目录后依然能对上。
//
// 一个文件里有三个类：两个条目类只是这张表的序列化行，离开表没有意义，沿用本文件原来的写法放在一起。

using System.Collections.Generic;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>源材质与它对应的 Toon 材质，一一配对。</summary>
    [System.Serializable]
    public sealed class ToonMaterialPair
    {
        // 字段私有 + 对外只读属性（csharp-code.md「序列化与暴露面」）：这是 Unity 序列化需要，
        // 不是给人改的数据结构，构造完就不该再动。
        [SerializeField]
        private string sourceId;

        [SerializeField]
        private string toonId;

        public ToonMaterialPair(string sourceId, string toonId)
        {
            this.sourceId = sourceId;
            this.toonId = toonId;
        }

        /// <summary>来源材质的 GlobalObjectId 字符串（子资产也能唯一标识，理由见转换器的注释）。</summary>
        public string SourceId
        {
            get { return sourceId; }
        }

        /// <summary>生成的 Toon 材质的 GlobalObjectId 字符串。</summary>
        public string ToonId
        {
            get { return toonId; }
        }
    }

    /// <summary>
    /// 一份源材质的临时调色旋钮值。中性值是色相 0、饱和度 1、明度 1。
    /// 在 Inspector 里直接改这张表、再跑「环境转 Toon 材质」即可生效；也可以在材质上拖好后跑「存回对应表」。
    /// </summary>
    [System.Serializable]
    public sealed class ToonGradeEntry
    {
        [SerializeField]
        [Tooltip("源材质的 GlobalObjectId。主钥匙。")]
        private string sourceId;

        [SerializeField]
        [Tooltip("源材质名。给人看的；glb 重导后 id 变了时，按它找回（只认唯一同名）。")]
        private string sourceName;

        [SerializeField]
        [Range(-180f, 180f)]
        [Tooltip("色相偏移，度。负值往黄、橙方向转（绿 → 黄绿 → 金）。")]
        private float hueShift;

        [SerializeField]
        [Range(0f, 2f)]
        [Tooltip("饱和度倍数，1 = 不变。")]
        private float saturation = 1f;

        [SerializeField]
        [Range(0f, 2f)]
        [Tooltip("明度倍数，1 = 不变。")]
        private float value = 1f;

        public ToonGradeEntry(string sourceId, string sourceName, float hueShift, float saturation, float value)
        {
            this.sourceId = sourceId;
            this.sourceName = sourceName;
            this.hueShift = hueShift;
            this.saturation = saturation;
            this.value = value;
        }

        public string SourceId
        {
            get { return sourceId; }
        }

        public string SourceName
        {
            get { return sourceName; }
        }

        public float HueShift
        {
            get { return hueShift; }
        }

        public float Saturation
        {
            get { return saturation; }
        }

        public float Value
        {
            get { return value; }
        }

        /// <summary>三个旋钮都在中性值（色相 0、饱和度 1、明度 1）。</summary>
        public bool IsNeutral
        {
            get { return Mathf.Approximately(hueShift, 0f) && Mathf.Approximately(saturation, 1f) && Mathf.Approximately(value, 1f); }
        }
    }

    /// <summary>
    /// 三渲二账本。资产落在生成目录内（<c>Art/Materials/Toon/</c>），但调色表是人写的数据，见文件头。
    /// </summary>
    public sealed class ToonMaterialMap : ScriptableObject
    {
        [SerializeField]
        private List<ToonMaterialPair> pairs = new List<ToonMaterialPair>();

        [SerializeField]
        [Tooltip("临时调色表：每份源材质的色相 / 饱和度 / 明度。转换器每次重写材质都从这里写回，转换从不清空它。")]
        private List<ToonGradeEntry> grades = new List<ToonGradeEntry>();

        /// <summary>全部配对。调用方只读遍历。</summary>
        public IReadOnlyList<ToonMaterialPair> Pairs
        {
            get { return pairs; }
        }

        /// <summary>全部调色条目。调用方只读遍历。</summary>
        public IReadOnlyList<ToonGradeEntry> Grades
        {
            get { return grades; }
        }

        /// <summary>记一次配对。同一个来源重复登记时覆盖旧的 Toon 标识。</summary>
        public void Set(string sourceId, string toonId)
        {
            for (int i = 0; i < pairs.Count; i++)
            {
                if (pairs[i].SourceId == sourceId)
                {
                    pairs[i] = new ToonMaterialPair(sourceId, toonId);
                    return;
                }
            }

            pairs.Add(new ToonMaterialPair(sourceId, toonId));
        }

        /// <summary>查来源对应的 Toon 标识。没有记录返回 null。</summary>
        public string GetToonId(string sourceId)
        {
            for (int i = 0; i < pairs.Count; i++)
            {
                if (pairs[i].SourceId == sourceId)
                {
                    return pairs[i].ToonId;
                }
            }

            return null;
        }

        /// <summary>查 Toon 材质对应的来源标识。反向查询（回退用）。</summary>
        public string GetSourceId(string toonId)
        {
            for (int i = 0; i < pairs.Count; i++)
            {
                if (pairs[i].ToonId == toonId)
                {
                    return pairs[i].SourceId;
                }
            }

            return null;
        }

        /// <summary>
        /// 清空**配对表**，重建时从零开始记。**调色表不动**——它是人写的权威数据，
        /// 配对表被一次空跑清空（实测踩到过）时，调色值不能跟着丢。
        /// </summary>
        public void ClearPairs()
        {
            pairs.Clear();
        }

        /// <summary>
        /// 查一份源材质的调色条目。先按 GlobalObjectId 找；找不到再按源材质名找，且只认**唯一**同名
        /// （glb 重导后子资产 id 可能变，名字还在；同名多条时宁可不认，也不猜）。都没有返回 null。
        /// </summary>
        public ToonGradeEntry FindGrade(string sourceId, string sourceName)
        {
            for (int i = 0; i < grades.Count; i++)
            {
                if (grades[i].SourceId == sourceId)
                {
                    return grades[i];
                }
            }

            ToonGradeEntry byName = null;
            for (int i = 0; i < grades.Count; i++)
            {
                if (grades[i].SourceName != sourceName)
                {
                    continue;
                }

                if (byName != null)
                {
                    return null;
                }

                byName = grades[i];
            }

            return byName;
        }

        /// <summary>写一条调色值。同一个来源（按 id）已有条目时覆盖。</summary>
        public void SetGrade(string sourceId, string sourceName, float hueShift, float saturation, float value)
        {
            var entry = new ToonGradeEntry(sourceId, sourceName, hueShift, saturation, value);
            for (int i = 0; i < grades.Count; i++)
            {
                if (grades[i].SourceId == sourceId)
                {
                    grades[i] = entry;
                    return;
                }
            }

            grades.Add(entry);
        }
    }
}
