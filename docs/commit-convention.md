# 提交信息规范

本项目的提交信息按本文写。看提交的人不看 diff 就能知道这次提交了什么、属于哪一类。

## 格式

```
type(scope): 一句话说清这次做了什么

- 改动点一
- 改动点二
```

## 类型

| type | 对应 | 何时用 |
| --- | --- | --- |
| `feat` | 增加模块 | 新模块、新功能、新配置项、新工具 |
| `fix` | 修复 | 修错误行为、崩溃、编译错误、引用断裂 |
| `refactor` | 修改 | 结构调整、重命名、拆分合并，行为不变 |
| `chore` | 修改 | 依赖、工程配置、harness、构建脚本 |
| `docs` | 说明 | 文档、注释、CLAUDE.md、规则文件 |
| `test` | 测试 | 只动测试 |

一次提交只做一类事。既加功能又修 bug，拆成两次。

## 标题

- `scope` 是模块名或区域，小写英文：`player`、`ui`、`ai`、`mcp`、`build`。跨多个就写最主要的那个。
- 中文，不超过 50 字，说结果不说过程。
- 标题能说清的小改动，不写正文。

## 正文

- 一个改动点一条，一行写完，格式「对象 + 做了什么」。对象是文件、类、模块或配置项。
- 顺序：新增 → 修改 → 修复 → 测试 / 说明。
- 只写落地的结果。排查过程、试过的方案、行数统计都不写。
- 有意留下没做的，放最后一条，以「留：」开头。

## 禁止

- 任何 AI 署名或会话链接：`Co-Authored-By: Claude`、`Claude-Session:`、`Generated with Claude Code`。守卫钩子会拒绝。
- 分节标题、编号列表、逐文件流水账、「本次改动包含以下内容」之类开场白。

## 示例

```
feat(player): 角色移动与跳跃基础模块

- PlayerMotor 新建，读 PlayerConfig 的速度与跳跃力，用 Rigidbody2D 驱动
- PlayerConfig ScriptableObject 加 moveSpeed、jumpForce、coyoteTime
- Data/Player/DefaultPlayerConfig 资产，默认值按原型手感
- 补 PlayerMotorTests 的落地判定与土狼时间用例
- 留：冲刺与墙跳下次提交
```

```
fix(ui): 暂停面板关闭后时间缩放没恢复
```
