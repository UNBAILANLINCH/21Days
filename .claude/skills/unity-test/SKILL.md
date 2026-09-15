---
name: unity-test
description: 跑 Unity EditMode / PlayMode 测试并汇报失败用例
disable-model-invocation: true
---

# /unity-test [EditMode|PlayMode] [过滤]

不带参数默认 EditMode、全部用例。按编辑器状态选路径：

- **编辑器已打开且 Unity MCP 已连接**：用 MCP 的 `run_tests`（v10 在 `testing` 工具组，未启用先用 `manage_tools` 打开），传 mode 与 filter。
- **编辑器未打开**：批处理模式。Unity Hub 默认安装目录下取 `ProjectSettings/ProjectVersion.txt` 里的版本：

  ```
  Unity.exe -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults Temp/test-results.xml -logFile Temp/test.log
  ```

  编辑器开着时工程被锁，批处理会直接失败，别重试。
- **编辑器打开但 MCP 未连接**：停下，让用户在编辑器里跑 Test Runner，或关闭编辑器后再来。

汇报：通过 / 失败 / 跳过数；每个失败用例给名字、断言信息、对应文件行。不贴整份日志。

完成标准：已读到结果文件或 MCP 返回，失败用例逐条列出；拿不到结果就说明卡在哪一步。
