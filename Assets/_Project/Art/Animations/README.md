# Animations — 动画

帧动画（`.anim`）与 Animator 控制器（`.controller`）放这里。
动画用的那些图放 `../Sprites/`，不要放这个目录。

**放进来会自动发生什么**：**没有自动规则**，这里只是约定的位置。
`.anim` / `.controller` 在 `.gitattributes` 里已按文本处理，能正常 diff 和提交。

**命名**：全小写 + 下划线，`类别_对象_动作`，例如 `chr_player_idle.anim`、`chr_player_run.controller`。
不用空格、不用中文。

**不要放**：源图（放 `../Sprites/`）、UI 面板的开关动画——面板的淡入淡出由框架统一做，
自己加 Animator 会和框架打架。

**不要在资源管理器里改名 / 移动 / 删除**——一律在 Unity 的 Project 窗口里操作，否则 `.meta` 掉队、引用静默失效。

详见 [`docs/artist-guide.md`](../../../../docs/artist-guide.md) 第 3、5 章，面板动画见第 6.6 节。
