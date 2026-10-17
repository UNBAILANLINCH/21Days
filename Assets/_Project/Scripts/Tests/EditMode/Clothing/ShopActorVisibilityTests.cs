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
