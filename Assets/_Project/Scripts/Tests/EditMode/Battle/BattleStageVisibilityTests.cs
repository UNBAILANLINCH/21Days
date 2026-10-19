// 职责：客栈战场局部显隐在重复进入、禁用和其它显示请求下准确恢复。
// 复用现有Battle测试夹具；新增文件因舞台显隐不属于相机或规则测试。
using System.Reflection;
using Game.Battle;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Battle
{
    public sealed class BattleStageVisibilityTests
    {
        [Test]
        public void Release_RepeatedAndDisabled_RestoresOriginalRenderingFlags()
        {
            var host = new GameObject("Stage");
            var character = new GameObject("Character");
            var renderer = character.AddComponent<SpriteRenderer>();
            var stage = host.AddComponent<BattleStage>();
            typeof(BattleStage).GetField("hideDuringBattle", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(stage, new[] { character, character });
            var hide = typeof(BattleStage).GetMethod("HideDressing", BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                renderer.enabled = false;
                hide.Invoke(stage, null);
                renderer.enabled = true; // 分组显示仍可更新自己的请求，但战斗镜头不应漏出人影。
                hide.Invoke(stage, null);
                Assert.That(renderer.forceRenderingOff, Is.True);
                stage.Release();
                stage.Release();
                Assert.That(renderer.forceRenderingOff, Is.False);
                Assert.That(renderer.enabled, Is.True);

                renderer.forceRenderingOff = true;
                hide.Invoke(stage, null);
                host.SetActive(false);
                Assert.That(renderer.forceRenderingOff, Is.True, "外部原已隐藏的角色不能被战斗收场显示");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(character);
            }
        }
    }
}
