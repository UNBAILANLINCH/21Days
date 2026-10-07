// 职责：场景作用域键的拼法——把「场景键」与「场景内实体标识」拼成一个全局唯一的键。
// 为什么新建：拼接口径只能有一处。JsonSaveService 的分区按类型存，键是扁平的字符串 / 数字，
//   所以「同一张地图里的同一个箱子」要在两份地图里各记一份，只能靠键本身带上场景。
//   拼法散在各调用点上，早晚会有人用 "/" 有人用 "_"，读存档时对不上。
using System;

namespace Game.World
{
    /// <summary>
    /// 场景作用域键。两段之间用 <see cref="Separator"/> 分隔，两段都不允许出现分隔符
    /// （<see cref="Scoped"/> 会当场抛 <see cref="ArgumentException"/>），所以拼接结果不会撞车。
    /// </summary>
    public static class SceneStateKey
    {
        /// <summary>场景键与实体标识之间的分隔符。</summary>
        public const string Separator = "::";

        /// <summary>
        /// 拼一个场景作用域键：<c>场景键::实体标识</c>（如 <c>human_jingyang::crate_inn_01</c>）。
        /// </summary>
        /// <param name="sceneKey">场景键（TbScene.scene_key），不能为空。</param>
        /// <param name="entryId">
        /// 场景内实体标识（箱子键 / 怪 id），不能为空。**必须是稳定且场景内唯一的字符串**：
        /// 它进存档，改了就等于换了一个实体。
        /// </param>
        /// <exception cref="ArgumentException">两段有一为空，或实体标识里带了 <see cref="Separator"/>。</exception>
        public static string Scoped(string sceneKey, string entryId)
        {
            if (string.IsNullOrEmpty(sceneKey))
            {
                throw new ArgumentException("场景键不能为空。", nameof(sceneKey));
            }

            if (string.IsNullOrEmpty(entryId))
            {
                throw new ArgumentException("实体标识不能为空。", nameof(entryId));
            }

            // 实体标识里再出现一次分隔符就会拼出两义键（a::b::c 读不回原样），当场拦掉。
            if (entryId.IndexOf(Separator, StringComparison.Ordinal) >= 0)
            {
                throw new ArgumentException(
                    $"实体标识「{entryId}」里不能出现分隔符「{Separator}」（它会和场景键拼出来的键混淆）。", nameof(entryId));
            }

            return sceneKey + Separator + entryId;
        }

        /// <summary>
        /// 场景作用域键的前缀（<c>场景键::</c>）。给「这一场里已开过哪些箱子」这类批量查询用：
        /// 分区的键是扁平列表，按前缀筛才分得清是哪一场的。
        /// </summary>
        /// <exception cref="ArgumentException">场景键为空。</exception>
        public static string PrefixOf(string sceneKey)
        {
            if (string.IsNullOrEmpty(sceneKey))
            {
                throw new ArgumentException("场景键不能为空。", nameof(sceneKey));
            }

            return sceneKey + Separator;
        }
    }
}
