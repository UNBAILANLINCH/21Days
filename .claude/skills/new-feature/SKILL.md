---
name: new-feature
description: 新玩法模块的标准流程：定范围 → 设计要点 → 实现 → 接线 → 验证 → 待审清单
disable-model-invocation: true
---

# /new-feature <模块名>

1. **定范围**：按 `CLAUDE.md` 目录约定，用一段话复述这个模块做什么、不做什么，列出将新建 / 修改的文件路径（全部在 `Assets/_Project/` 下）。有歧义的地方合并成一次提问，问完再动手。
2. **设计要点**：3 到 6 条。哪些数据进 ScriptableObject、运行时类各自的职责、依赖方向、与已有模块的接口。只写决定，不写实现。若模块已有 `ai-docs/docs/modules/<模块>/` guide，先读。
3. **实现**：先数据类与接口，再 MonoBehaviour。目录或 asmdef 不存在就先建。
4. **接线**：Unity MCP 已连接就用它建物体、挂组件、赋引用；未连接就只写脚本，把接线步骤列给用户在编辑器里做。
5. **验证**：控制台零编译错误；能抽成纯逻辑的部分写 EditMode 测试并跑 `/unity-test`。保存时 project-lint 自动跑，零违规；把改动范围交给 `code-reviewer` 子代理（model sonnet）做模块级审查。
6. **收尾**：跑 `/review-change`，停下等审。

完成标准：第 1 步列出的每个文件都已落地，或逐个说明未做原因；控制台零编译错误；清单已列出且未提交。
