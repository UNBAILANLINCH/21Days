---
description: Unity 资产（场景、预制体、ScriptableObject、asmdef、meta）的编辑与版本控制约定。编辑 Assets/ 下非 .cs 文件时适用。
paths: ["Assets/**/*.unity", "Assets/**/*.prefab", "Assets/**/*.asset", "Assets/**/*.asmdef", "Assets/**/*.mat", "Assets/**/*.anim", "Assets/**/*.controller"]
globs: ["Assets/**/*.unity", "Assets/**/*.prefab", "Assets/**/*.asset", "Assets/**/*.asmdef", "Assets/**/*.mat", "Assets/**/*.anim", "Assets/**/*.controller"]
alwaysApply: false
---

# Unity 资产规则

## 场景与预制体

- 改场景 / 预制体**优先通过 Unity MCP 在编辑器里做**（`manage_gameobject`、`manage_scene`），让 Unity 自己序列化。直接改 `.unity` / `.prefab` 文本只在用户明确要求时，且只改明确的字段，不动 `fileID` / `guid`。
- 可复用的对象做成预制体，场景里只放实例；预制体改动走 Prefab 本身，不在场景实例上堆 override。
- 场景尽量小而多（主场景 + 功能子场景 Additive），减少多人/多会话同时改同一场景的合并冲突。

## ScriptableObject 配置

- 类定义在 `Scripts/Runtime/<Module>/`，资产放 `Assets/_Project/Data/<Module>/`。
- 用 `[CreateAssetMenu(menuName = "21Days/<Module>/<Name>")]` 统一菜单前缀。
- 配置只读：运行时不修改 SO 字段（修改会写回资产），需要运行时状态就复制到普通类。

## asmdef

- 四个程序集：`Game.Runtime`、`Game.Editor`、`Game.Tests.EditMode`、`Game.Tests.PlayMode`；依赖方向见 `project-root.md`。
- 测试 asmdef 勾 `Test Assemblies`，引用 `UnityEngine.TestRunner` / `UnityEditor.TestRunner`；Runtime 不引用它们。

## .meta 与版本控制

- `.meta` 由 Unity 生成，随资产一起提交；移动/删除/重命名资产用 `git mv` / `git rm` 连同 `.meta`。
- 新建文件后要等 Unity 刷新生成 `.meta` 再提交，否则别人打开工程会 GUID 变动、引用断裂。
- 序列化文件按文本处理（`.gitattributes` 已设 `text eol=lf`）；大素材（图、音、模型）标 `binary`，体积上来再考虑 git-lfs。

## 检查清单

- [ ] 没有手改 `fileID` / `guid`。
- [ ] 资产改动带 `.meta`，没有孤儿 `.meta`。
- [ ] SO 资产在 `Data/` 下，类在对应模块目录。
- [ ] 新增 asmdef 的引用方向合规。
