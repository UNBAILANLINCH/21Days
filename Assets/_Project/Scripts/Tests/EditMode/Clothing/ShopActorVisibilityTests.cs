// 职责：店铺显隐只切Renderer，不禁用角色根或重新创建对象。
using Game.ShopPlaytest;
using Game.Taming;
using NUnit.Framework;
using UnityEngine;
namespace Game.Tests.EditMode.Clothing
{
    public sealed class ShopActorVisibilityTests
    {
        [Test]
        public void DeadActor_StaysHiddenAcrossShopChanges_WithoutHidingWornVisual()
        {
            var root = new GameObject("actor");
            var player = new GameObject("player");
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.transform.SetParent(root.transform);
            var label = new GameObject("name").AddComponent<SpriteRenderer>();
            label.transform.SetParent(root.transform);
            var actor = root.AddComponent<ShopPlaytestActor>();
            try
            {
                actor.SetVisible(true);
                actor.SetAlive(false);
                Assert.That(actor.IsVisible, Is.False);
                Assert.That(visual.GetComponent<Renderer>().enabled, Is.False);
                Assert.That(label.enabled, Is.False);
                Assert.That(root.activeInHierarchy, Is.True, "保留角色根与模型引用");
                visual.transform.SetParent(player.transform);
                visual.GetComponent<Renderer>().enabled = true;
                actor.SetVisible(false); actor.SetVisible(true);
                Assert.That(actor.IsVisible, Is.False, "切店不能复活死者");
                Assert.That(label.enabled, Is.False);
                Assert.That(visual.GetComponent<Renderer>().enabled, Is.True, "已穿到玩家的皮不随来源隐藏");
                actor.SetAlive(true);
                Assert.That(actor.IsVisible, Is.True, "模型重置后恢复原分组可见性");
                Assert.That(label.enabled, Is.True);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(player); }
        }
        [Test]
        public void HideShow_KeepsActorAvailableAndRestoresOriginalRendererMask()
        {
            var root = new GameObject("actor"); var visible = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visible.transform.SetParent(root.transform); var proxy = root.AddComponent<SpriteRenderer>(); proxy.enabled = false;
            var actor = root.AddComponent<ShopPlaytestActor>(); var identity = root.AddComponent<TamingActor>();
            int disabled = 0; identity.OnAvailabilityChanged += (_, available) => { if (!available) disabled++; };
            try
            {
                actor.SetVisible(false);
                Assert.That(root.activeInHierarchy, Is.True); Assert.That(disabled, Is.Zero);
                Assert.That(visible.GetComponent<Renderer>().enabled, Is.False);
                actor.SetVisible(true);
                Assert.That(visible.GetComponent<Renderer>().enabled, Is.True);
                Assert.That(proxy.enabled, Is.False, "原本不可见的逻辑纸片不能被显隐误打开");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
