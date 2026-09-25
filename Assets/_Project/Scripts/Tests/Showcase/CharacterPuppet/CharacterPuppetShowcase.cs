// 职责：回放拼接小人的待机 → 向右走 → 向左走 → 停下，看动画切换与翻面。
// 新建原因：CharacterPuppet 是新模块，按 module-verify.md 每模块一份 Showcase。
using System.Collections;
using Game.CharacterPuppet;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.Showcase.CharacterPuppet
{
    [Category("Showcase")]
    public sealed class CharacterPuppetShowcase : ShowcaseScenario
    {
        private const float MoveSpeed = 1.5f;
        private const float MoveSeconds = 2f;

        protected override string Module => "CharacterPuppet";
        protected override string ScenePath => "Assets/_Project/Scenes/Verify/CharacterPuppet.unity";
        protected override bool LoadBootScene => false;

        private bool moveDone;

        [UnityTest]
        public IEnumerator IdleWalkTurnStop_PlaysMatchingAnimation()
        {
            var root = FindRequired<Transform>("Puppet");
            var puppet = FindRequired<ChibiPuppet>("ChibiPuppet_Player");
            Coroutine move = null;
            try
            {
                yield return Step("原地站 2 秒", null, 2f);
                yield return Check("播放待机动画（呼吸起伏）", () => IsState(puppet, "Idle"), 1f);
                yield return Snapshot("待机");

                yield return Step("以 1.5 单位/秒向右移动 2 秒", () =>
                    move = puppet.StartCoroutine(MoveRoot(root, Vector3.right * MoveSpeed, MoveSeconds)), 0f);
                yield return Check("切到走路动画且面朝右", () => IsState(puppet, "Walk") && puppet.transform.localScale.x > 0f, 1f);
                yield return Snapshot("向右走");
                yield return WaitUntil("向右移动结束", () => moveDone, MoveSeconds + 3f);

                yield return Step("以 1.5 单位/秒向左移动 2 秒", () =>
                    move = puppet.StartCoroutine(MoveRoot(root, Vector3.left * MoveSpeed, MoveSeconds)), 0f);
                yield return Check("翻面朝左并继续走路", () => IsState(puppet, "Walk") && puppet.transform.localScale.x < 0f, 1f);
                yield return Snapshot("向左走");
                yield return WaitUntil("向左移动结束", () => moveDone, MoveSeconds + 3f);

                yield return Step("停下", null, 1f);
                yield return Check("回到待机动画，保持朝左", () => IsState(puppet, "Idle") && puppet.transform.localScale.x < 0f, 1.5f);
                yield return Snapshot("停下待机");

                // 世界时停（对话期间 Time.timeScale = 0）：Animator 走 unscaled time，待机呼吸应继续播放而不是定格。
                yield return Step("触发世界时停（模拟对话）", () => Time.timeScale = 0f, 0f);
                try
                {
                    yield return Wait(0.5f);
                    float idleTimeBefore = GetIdleNormalizedTime(puppet);
                    for (int i = 0; i < 5; i++)
                    {
                        yield return null;
                    }

                    yield return Check("时停期间待机呼吸动画仍在播放（未定格）",
                        () => IsState(puppet, "Idle") && GetIdleNormalizedTime(puppet) > idleTimeBefore, 1f);
                    yield return Snapshot("时停待机");
                }
                finally
                {
                    // 无论检查是否通过都要恢复，避免这条用例把 timeScale 泄漏给同一批次的其他测试。
                    Time.timeScale = 1f;
                }
            }
            finally
            {
                if (move != null && puppet != null)
                {
                    puppet.StopCoroutine(move);
                }
            }
        }

        private static bool IsState(ChibiPuppet puppet, string state) =>
            puppet.Animator != null && puppet.Animator.GetCurrentAnimatorStateInfo(0).IsName(state);

        private static float GetIdleNormalizedTime(ChibiPuppet puppet) =>
            puppet.Animator == null ? 0f : puppet.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;

        // 逐帧推根节点，模拟角色移动；小人只从位移反推动画，不知道是谁在推。
        private IEnumerator MoveRoot(Transform root, Vector3 velocity, float seconds)
        {
            moveDone = false;
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                yield return null;
                elapsed += Time.deltaTime;
                root.position += velocity * Time.deltaTime;
            }

            moveDone = true;
        }
    }
}
