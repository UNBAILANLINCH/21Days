---
description: 项目全局约定：目录结构、asmdef 依赖方向、生成物边界、加能力的顺序。任何新增文件、放置代码、跨模块调用的判断都适用。
paths: []
globs: []
alwaysApply: true
---

# 项目根规则

## 目录与 asmdef 依赖方向（硬约束）

```
Game.Tests.EditMode / Game.Tests.PlayMode   ── 只能引用 Game.Runtime（测试不被任何人引用）
Game.Editor                                 ── 只能引用 Game.Runtime（Editor 代码不进构建）
Game.Runtime                                ── 不引用 Editor / Tests；不 using UnityEditor
```

- Runtime 里需要编辑器专用逻辑时用 `#if UNITY_EDITOR` 包住，并且只限调试/Gizmos，不放业务。
- 模块之间：`Scripts/Runtime/<Module>/` 内部自洽；跨模块只通过对方的公开接口 / 事件 / ScriptableObject 引用，不互相 `GetComponent` 到私有实现。

## 目录约定

| 目录 | 放什么 |
| --- | --- |
| `Assets/_Project/Scripts/Runtime/<Module>/` | 游戏逻辑，一个模块一个目录，命名空间 `Game.<Module>` |
| `Assets/_Project/Scripts/Editor/` | 编辑器工具、自定义 Inspector |
| `Assets/_Project/Scripts/Tests/{EditMode,PlayMode}/` | 测试；EditMode 优先 |
| `Assets/_Project/Data/` | ScriptableObject 配置资产 |
| `Assets/_Project/{Prefabs,Scenes,Art,Audio}/` | 资源 |
| `Assets/Scenes/`、`Assets/Settings/` | 模板自带（SampleScene、URP 配置），原位不动 |
| `ai-docs/` | 知识层：模块三件套、catalog、pitfalls |
| `PRP/` | 复杂功能的 PRD / PRP / tasks |

第一个玩法模块落地时再创建这套结构，asmdef 随之建立；空目录不预建。

## 生成物边界（钩子强制）

`Library/ Temp/ Logs/ obj/ UserSettings/`、`*.meta`、`*.csproj/*.sln`、`packages-lock.json` 由 Unity 生成，不手改。
移动、删除、重命名资产用 `git mv` / `git rm` 连同 `.meta` 一起；新建 `.cs` 后要让 Unity 刷新生成 `.meta` 再提交。

## 加能力的顺序（不能跳）

1. **复用**：已有脚本 / 组件 / ScriptableObject 换个参数能不能用？
2. **扩展**：加进已有文件行不行？尺子是职责不是行数，名字和职责说不通就别硬塞。
3. **新增**：到这一步才建新文件，文件头注释写明前两步为何不行。

## 检查清单

- [ ] 新文件在正确的目录与 asmdef 下，命名空间与目录一致。
- [ ] 没有 Runtime → Editor / Tests 的反向引用。
- [ ] 没有手改生成物；新 `.cs` 有配套 `.meta`。
- [ ] 文件内没有本机用户名、绝对路径、邮箱。
