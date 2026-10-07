// 职责：钉住 `Gameplay/Execute` 这个动作**存在、在 Gameplay 图里、绑了 F**（PRP §3.6）。
//   不要求在 EditMode 里模拟按键（PRP 明说），所以这里只查资产与生成物两处声明。
// 为什么新建：输入资产是共享点，绑错图 / 拼错键 / 忘了重新生成 `GameInput.cs` 都不会有任何报错，
//   只会表现成「按 F 没反应」；这条用例把三种情况都在编译期或测试期拎出来。
// 负对照：UI 图里不许有 Execute（防止动作加错图）；动作类型必须是 Button（按下沿，不是 Value）。
using System.IO;
using Game.Core.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Tests.EditMode.Stealth
{
    public sealed class ExecuteInputBindingTests
    {
        /// <summary>输入资产的真源路径（从 <c>Application.dataPath</c> 推，不写死本机绝对路径）。</summary>
        private static string AssetPath =>
            Path.Combine(Application.dataPath, "_Project/Data/Input/GameInput.inputactions");

        [Test]
        public void GameplayMap_HasExecuteAction_BoundToKeyboardFAndAGamepadButton()
        {
            ReadAsset(asset =>
            {
                InputActionMap gameplay = asset.FindActionMap("Gameplay", false);
                Assert.That(gameplay, Is.Not.Null, "Gameplay 图必须还在");

                InputAction execute = gameplay.FindAction("Execute", false);
                Assert.That(execute, Is.Not.Null,
                    "Gameplay 图里要有 Execute 动作（PRP/stealth-execution §2.2）");
                Assert.That(execute.type, Is.EqualTo(InputActionType.Button), "处决是按下沿触发的交互，不是 Value");

                bool hasF = false;
                bool hasGamepad = false;
                for (int i = 0; i < execute.bindings.Count; i++)
                {
                    InputBinding binding = execute.bindings[i];
                    if (binding.isComposite || binding.isPartOfComposite)
                    {
                        continue;
                    }

                    string path = binding.path;
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }

                    if (path == "<Keyboard>/f")
                    {
                        hasF = true;
                    }
                    else if (path.StartsWith("<Gamepad>", System.StringComparison.Ordinal))
                    {
                        hasGamepad = true;
                    }
                }

                Assert.That(hasF, Is.True, "策划原件 06_怪物状态与交互设计文档.md:69「玩家可以在怪物背后按F处决」");
                Assert.That(hasGamepad, Is.True, "手柄也要有一个键（PRP §2.2）");
            });
        }

        /// <summary>负对照：动作只许出现在 Gameplay 图（加进 UI 图会让菜单里按 F 也去处决）。</summary>
        [Test]
        public void ExecuteAction_IsNotInTheUiMap()
        {
            ReadAsset(asset =>
            {
                InputActionMap ui = asset.FindActionMap("UI", false);
                Assert.That(ui, Is.Not.Null);
                Assert.That(ui.FindAction("Execute", false), Is.Null, "UI 图里不该有 Execute");
            });
        }

        /// <summary>
        /// 生成物这一侧：`GameInput.cs` 必须已经重新生成（改了 `.inputactions` 而没重生成时，
        /// 本组件会找不到动作、现场表现为「按 F 没反应」）。**生成物不许手改**，所以这条也顺带证明它没被手改坏。
        /// </summary>
        [Test]
        public void GeneratedWrapper_ExposesTheExecuteAction()
        {
            var wrapper = new GameInput();
            try
            {
                Assert.That(wrapper.asset.FindAction("Gameplay/Execute", false), Is.Not.Null,
                    "GameInput.cs 里没有 Gameplay/Execute：等 Unity 重新生成，别手改生成物");
                Assert.That(wrapper.Gameplay.Execute, Is.Not.Null, "类型化访问器也要在");
            }
            finally
            {
                // **不能调 wrapper.Dispose()**：生成物的 Dispose 走 UnityEngine.Object.Destroy，
                // 在 EditMode 里会打一条 Error（测试框架据此判失败）。这里自己用 DestroyImmediate 收掉资产。
                Object.DestroyImmediate(wrapper.asset);
            }
        }

        /// <summary>
        /// 共享点纪律：**本波只新增、不改既有绑定**。守门用一条「Interact 的三条绑定原样还在」的断言——
        /// F 与 Interact 共用同一个键是**有意保留**的（两个动作各自有目标门槛，见 ExecutionInteractor 的文件头），
        /// 若有人为了「消掉重复」把 F 从 Interact 上摘掉，这条会亮。
        /// </summary>
        [Test]
        public void InteractBindings_AreLeftAlone()
        {
            ReadAsset(asset =>
            {
                InputAction interact = asset.FindActionMap("Gameplay", false).FindAction("Interact", false);
                Assert.That(interact, Is.Not.Null);

                bool e = false;
                bool f = false;
                bool south = false;
                for (int i = 0; i < interact.bindings.Count; i++)
                {
                    string path = interact.bindings[i].path;
                    if (path == "<Keyboard>/e") e = true;
                    else if (path == "<Keyboard>/f") f = true;
                    else if (path == "<Gamepad>/buttonSouth") south = true;
                }

                Assert.That(e && f && south, Is.True,
                    "Interact 的 E / F / South 是既有约定，本波只允许新增 Execute，不许动它");
            });
        }

        private static void ReadAsset(System.Action<InputActionAsset> assert)
        {
            Assert.That(File.Exists(AssetPath), Is.True, $"找不到输入资产：{AssetPath}");
            InputActionAsset asset = InputActionAsset.FromJson(File.ReadAllText(AssetPath));
            try
            {
                assert(asset);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
