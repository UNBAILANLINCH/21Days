// 职责：钉住战斗相机交接——接管时关掉开着的世界相机、打开战斗相机；交还时只恢复进来前的状态
//   （本来关着的相机不擅自打开、战斗相机回到原开关），可重复交还、期间被销毁的相机跳过；底色从世界相机拷过来。
// 为什么新建：BattleCameraHandoff 是 W2a 新增；一个被测类一个测试类。EditMode 直接 new 相机，不依赖任何场景资产。
using System.Collections.Generic;
using Game.Battle;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Battle
{
    public sealed class BattleCameraHandoffTests
    {
        private readonly List<GameObject> created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in created)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            created.Clear();
        }

        [Test]
        public void Take_DisablesEnabledWorldCameras_EnablesStage()
        {
            Camera world = NewCamera("World", true);
            Camera stage = NewCamera("Stage", false);
            var handoff = new BattleCameraHandoff();

            handoff.Take(stage, new[] { world });

            Assert.That(world.enabled, Is.False, "世界相机让位");
            Assert.That(world.gameObject.activeSelf, Is.True, "只关 Camera 组件，不关物体（AudioListener 照常）");
            Assert.That(stage.enabled, Is.True);
            Assert.That(handoff.SwitchedOffCount, Is.EqualTo(1));
        }

        [Test]
        public void Release_RestoresOnlyWhatWasOnBefore()
        {
            // 负对照：进来前就关着的相机（如 Boot 兜底相机已让位），交还时不擅自打开。
            Camera world = NewCamera("World", true);
            Camera alreadyOff = NewCamera("AlreadyOff", false);
            Camera stage = NewCamera("Stage", false);
            var handoff = new BattleCameraHandoff();

            handoff.Take(stage, new[] { world, alreadyOff });
            handoff.Release();

            Assert.That(world.enabled, Is.True, "被我们关掉的重新打开");
            Assert.That(alreadyOff.enabled, Is.False, "本来关着的保持关着");
            Assert.That(stage.enabled, Is.False, "战斗相机回到接管前（关）");
            Assert.That(handoff.IsActive, Is.False);
        }

        [Test]
        public void Release_IsIdempotent_AndSkipsDestroyedCameras()
        {
            Camera world = NewCamera("World", true);
            Camera doomed = NewCamera("Doomed", true);
            Camera stage = NewCamera("Stage", false);
            var handoff = new BattleCameraHandoff();
            handoff.Take(stage, new[] { world, doomed });
            Object.DestroyImmediate(doomed.gameObject);

            Assert.DoesNotThrow(() => handoff.Release());
            Assert.DoesNotThrow(() => handoff.Release(), "重复交还是空操作");
            Assert.That(world.enabled, Is.True);
        }

        [Test]
        public void Take_Twice_DoesNotOverwriteTheRecord()
        {
            Camera world = NewCamera("World", true);
            Camera stage = NewCamera("Stage", false);
            var handoff = new BattleCameraHandoff();
            handoff.Take(stage, new[] { world });

            bool second = handoff.Take(stage, new Camera[0]);
            handoff.Release();

            Assert.That(second, Is.False, "接管中再接管被忽略");
            Assert.That(world.enabled, Is.True, "第一次关掉的名单还在，照样恢复");
        }

        [Test]
        public void CopyLook_CopiesClearAndBackground()
        {
            Camera world = NewCamera("World", true);
            world.clearFlags = CameraClearFlags.SolidColor;
            world.backgroundColor = new Color(0.8f, 0.76f, 0.68f, 1f);
            Camera stage = NewCamera("Stage", false);
            stage.clearFlags = CameraClearFlags.Skybox;

            BattleCameraHandoff.CopyLook(world, stage);

            Assert.That(stage.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
            Assert.That(stage.backgroundColor, Is.EqualTo(world.backgroundColor), "背景色 = 世界相机（SampleScene 里等于雾色）");
            Assert.DoesNotThrow(() => BattleCameraHandoff.CopyLook(null, stage), "没有世界主相机时什么都不做");
        }

        private Camera NewCamera(string name, bool enabled)
        {
            var go = new GameObject(name);
            created.Add(go);
            var camera = go.AddComponent<Camera>();
            camera.enabled = enabled;
            return camera;
        }
    }
}
