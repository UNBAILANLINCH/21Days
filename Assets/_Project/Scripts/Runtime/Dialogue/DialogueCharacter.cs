// 职责：角色表情行的运行时只读索引；不把内容混入表现 SO。
using System;
using System.Collections.Generic;

namespace Game.Dialogue
{
    public sealed class DialogueCharacter
    {
        private readonly Dictionary<string, string> sprites;
        public DialogueCharacter(string id, string displayName, string defaultExpression, IDictionary<string, string> expressions)
        {
            if (string.IsNullOrWhiteSpace(id) || displayName == null || expressions == null ||
                defaultExpression == null || !expressions.ContainsKey(defaultExpression))
                throw new ArgumentException("角色或默认表情无效");
            sprites = new Dictionary<string, string>(expressions, StringComparer.Ordinal);
            foreach (var pair in sprites)
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                    throw new ArgumentException("表情资源地址不可为空");
            Id = id;
            DisplayName = displayName;
            DefaultExpression = defaultExpression;
        }
        public string Id { get; }
        public string DisplayName { get; }
        public string DefaultExpression { get; }
        public string DefaultSprite => sprites[DefaultExpression];
        public bool TrySprite(string expression, out string key) => sprites.TryGetValue(expression, out key);
        public IEnumerable<string> SpriteKeys => sprites.Values;
    }
}
