// 纯运动规则测试不会播放 Animator；在隔离对象上验证这两套拆件的真实状态切换与时停待机。
#if UNITY_EDITOR
using System.Collections;
using Game.CharacterPuppet;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode.CharacterPuppet
{
    public sealed class CustomerPartsAnimationTests
    {
        [UnityTest]
        public IEnumerator Motion_ChangesIdleWalkRunAndKeepsIdleDuringPause()
        {
            float previousTimeScale = Time.timeScale;
            GameObject actor = null;
            try
            {
                Time.timeScale = 1;
                foreach (string id in new[] { "customers", "drunk_customer" })
                {
                    actor = new GameObject("PartsAnimationTest");
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Characters/Chibi_" + id + ".prefab");
                    Assert.NotNull(asset);
                    var instance = Object.Instantiate(asset, actor.transform);
                    var puppet = instance.GetComponent<ChibiPuppet>();
                    var motion = instance.GetComponent<ChibiPuppetMotion>();
                    var data = new SerializedObject(motion);
                    data.FindProperty("trackedRoot").objectReferenceValue = actor.transform;
                    data.ApplyModifiedPropertiesWithoutUndo();
                    var animator = puppet.Animator;
                    yield return WaitForState(animator, "Idle", id + "出生");
                    foreach (float speed in new[] { 3f, 5f })
                    {
                        float until = Time.realtimeSinceStartup + .4f;
                        while (Time.realtimeSinceStartup < until)
                        {
                            actor.transform.position += Vector3.right * (speed * Time.deltaTime);
                            yield return null;
                        }

                        Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName(speed == 3 ? "Walk" : "Run"),
                            id + "速度=" + speed + " moving=" + puppet.IsMoving + " running=" + puppet.IsRunning);
                        Assert.That(animator.GetFloat("Speed"), Is.InRange(.8f, 1.6f));
                    }

                    yield return WaitForState(animator, "Idle", id + "停步");
                    float before = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                    Time.timeScale = 0;
                    yield return new WaitForSecondsRealtime(.25f);
                    Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), id + "时停");
                    Assert.Greater(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, before, id);
                    Time.timeScale = 1;
                    Object.Destroy(actor); actor = null;
                    yield return null;
                }
            }
            finally
            {
                Time.timeScale = previousTimeScale;
                if (actor != null) Object.Destroy(actor);
            }
        }

        [UnityTest]
        public IEnumerator DrunkCustomer_IdleAutomaticallyDrinksAndMovementInterruptsIt()
        {
            float previousTimeScale = Time.timeScale;
            GameObject actor = null;
            try
            {
                Time.timeScale = 1;
                actor = new GameObject("DrunkAnimationTest");
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Characters/Chibi_drunk_customer.prefab");
                Assert.NotNull(asset);
                var instance = Object.Instantiate(asset, actor.transform);
                var puppet = instance.GetComponent<ChibiPuppet>();
                instance.GetComponent<ChibiPuppetMotion>().enabled = false;
                yield return WaitForState(puppet.Animator, "Drink", "醉酒顾客自动喝酒", 4.5f);
                puppet.SetMoving(true, false, 1);
                yield return WaitForState(puppet.Animator, "Walk", "移动打断喝酒");
            }
            finally
            {
                Time.timeScale = previousTimeScale;
                if (actor != null) Object.Destroy(actor);
            }
        }

        private static IEnumerator WaitForState(Animator animator, string state, string context, float timeout = 2f)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!animator.GetCurrentAnimatorStateInfo(0).IsName(state) && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName(state),
                context + "期望=" + state + "，实际hash=" + animator.GetCurrentAnimatorStateInfo(0).fullPathHash);
        }
    }
}
#endif
