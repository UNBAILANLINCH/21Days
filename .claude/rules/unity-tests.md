---
description: Unity Test Framework 测试规范：EditMode 优先、命名、结构、怎么跑。编辑 Scripts/Tests/ 下文件时适用。
paths: ["Assets/_Project/Scripts/Tests/**"]
globs: ["Assets/_Project/Scripts/Tests/**"]
alwaysApply: false
---

# Unity 测试规则

## 选 EditMode 还是 PlayMode

- **能抽成纯逻辑的先抽出来，写 EditMode 测试**：不依赖场景、不依赖帧循环，跑得快、编辑器开着也能跑。
- 只有必须经过 Unity 生命周期（物理、协程、动画、场景加载）的才写 PlayMode，用 `[UnityTest]` + `yield return null`。
- 一个玩法模块至少一条 EditMode 测试覆盖核心规则。

## 结构与命名

- 目录：`Scripts/Tests/EditMode/<Module>/`、`Scripts/Tests/PlayMode/<Module>/`，与被测模块同名。
- 测试类 `<被测类>Tests`；方法 `<行为>_<条件>_<期望>`（如 `TakeDamage_WhenShieldActive_ReducesShieldFirst`）。
- 用 `[SetUp]` 建被测对象，`[TearDown]` 销毁 `new GameObject` 出来的东西（PlayMode 尤其）。
- 断言用 NUnit `Assert.That(actual, Is.EqualTo(expected))`，一条测试一个关注点。

## 怎么跑

- `/unity-test [EditMode|PlayMode] [过滤]`：编辑器开着且 MCP 已连 → MCP `run_tests`；编辑器没开 → batchmode。
- 编辑器开着时 batchmode 会因工程锁失败，不要重试，换 MCP 或让用户在 Test Runner 里跑。
- 测试失败如实报告，**不靠改弱断言凑绿**。

## 检查清单

- [ ] 纯逻辑部分有 EditMode 测试。
- [ ] 测试不依赖 SampleScene 或具体资产路径。
- [ ] PlayMode 测试创建的对象在 TearDown 清理。
