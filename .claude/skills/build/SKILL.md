---
name: build
description: 本机出包（Windows 端游 / Android 手游）：先确认编辑器已关闭，再调 scripts/build.ps1，成功报产物路径与体积，失败只摘日志里的前几条错误
disable-model-invocation: true
---

# /build [Windows|Android] [版本号]

不带参数默认 `Windows`、不指定版本（沿用 `ProjectSettings` 里的版本）。

## 1. 先判断编辑器开没开（不能跳）

`Temp/UnityLockfile` 存在 = 编辑器正开着，工程被锁，批处理构建进不去。

```powershell
Test-Path Temp/UnityLockfile
```

**开着就停下**，让用户关掉 Unity 编辑器后重新 `/build`。

（工程没有 CI 可退——2026-09-16 已搁置，原因见 `docs/ci-setup.md`。本机是唯一出包路径。）

**不重试、不绕**（不要删锁文件、不要杀 Unity 进程、不要试 `-force` 之类的花样）。
`scripts/build.ps1` 自己也会拦一道，检测到编辑器在跑就 **exit 2** —— 撞上 exit 2 同样按这条处理。

## 2. 跑构建

编辑器没开才走到这一步：

```powershell
powershell -NoProfile -File scripts/build.ps1 -Target <Windows|Android> [-Version <版本号>]
```

- `<Windows|Android>` 取用户给的第一个参数；第二个参数给了就接 `-Version`。
- 构建是长任务（冷启十几分钟很正常），别中途打断，也别因为没输出就重跑。
- 需要调试符号 / Profiler 时才加 `-Development`，用户没说就不要加。
- `-Release` 同理，**用户没说要上架 / 要测真机性能就不要加**，理由见下。

### 2.1 `-Release`：Android 的可上架配置（默认不加）

| | 不加（默认） | 加 `-Release` |
| --- | --- | --- |
| 脚本后端 | Mono | IL2CPP |
| CPU 架构 | 沿用工程设置（当前 ARMv7） | 仅 ARM64 |
| 能不能满足 Play 的 64 位要求 | 不能 | 能 |
| 耗时（2026-09-16 实测） | 75 秒 | **268 秒，约 3.6 倍**（IL2CPP 要把 C# 转译成 C++ 再用 NDK 编原生库） |
| 包体（同上） | 33.4 MB | 41.6 MB |

**什么时候加**：要出上架候选包、或者要在真机上测真实性能（Mono 和 IL2CPP 的性能不是一回事）。
**什么时候不加**：其余全部场景 —— 日常自测、看美术效果、验流程，加了只是白等。

两件事得说在前头，别让人误会：

- `-Release` 只解决 **64 位**这一条。导出的仍是 **APK**，而 Google Play 对新应用要 **AAB**，
  包也**没有签名**。所以「加了 `-Release`」≠「能上架」，详见 `docs/developer-guide.md` 第 14 章。
- 它**临时**改 `ProjectSettings`（IL2CPP + ARM64），`BuildScript` 在 `finally` 里无条件改回，
  并按构建前的原始字节回写 `ProjectSettings.asset`。构建完顺手 `git status` 扫一眼，
  **正常情况下工作区不该多出 `ProjectSettings/ProjectSettings.asset` 这条改动**；真多出来了，
  说明恢复那步出了问题，`git checkout -- ProjectSettings/ProjectSettings.asset` 还原并报告。

## 3. 汇报

**成功**：给产物路径与体积，一句话说完。默认产物是 `Builds/Windows/21Days.exe` 与 `Builds/Android/21Days.apk`。

```powershell
Get-ChildItem -Recurse Builds/Windows | Measure-Object -Property Length -Sum
```

例：`Builds/Windows/21Days.exe（连同 Data 目录共 182 MB）`。Android 则给 `.apk` 文件本身的大小。

Android 还要**带上本次的配置**（脚本最后一行 `实际生效 → [Build] 配置：…` 是 `BuildScript` 实读
`PlayerSettings` 后打的，以它为准）。例：`Builds/Android/21Days.apk（63 MB，Release：IL2CPP + ARM64）`。
只报体积不报配置，等于让人事后靠大小猜自己出的是哪种包。

**失败**：读日志的**尾部**（末尾 100～200 行足够）—— 文件名里的平台是小写，即
`Logs/build-windows.log` / `Logs/build-android.log`。挑出**前几条**真正的错误：

- 编译错误 → 给 `错误码 + 一句话 + 文件:行`（如 `CS0246: 找不到类型 FooService — Assets/_Project/Scripts/Runtime/Foo/Bar.cs:42`）；
- 构建期错误（缺场景、Android SDK/NDK 未配、签名、磁盘不足）→ 给那一行原文 + 一句中文解释 + 该去改哪儿。

**不贴整份日志**，也不要把同一个错误的十几行堆栈全搬过来。错误超过 5 条就报前 5 条并说明「还有 N 条同类」。
日志里如果出现本机路径，转述时替换成工程相对路径。

## 边界

- 只跑构建，**不改工程文件**。要改 Player Settings / 场景列表，先说明再单独做，不要顺手塞进这次打包。
- 不 `git add` / `git commit` 产物；`Builds/` 已在 `.gitignore` 里。
- 工程没有 CI（已搁置，见 `docs/ci-setup.md`），出给别人的版本也是本机打，注意包没签名、只适合小范围分发。

## 完成标准

编辑器开着时：已停下并要求用户关掉编辑器，没有任何重试。
编辑器没开时：构建进程已结束，且要么报出了产物路径与体积，要么给出了从日志里摘的前几条错误与对应文件行；卡在哪一步说不清就直说卡在哪，不猜结果。
