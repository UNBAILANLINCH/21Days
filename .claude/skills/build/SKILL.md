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

**开着就停下**，把两条路摆给用户，等用户选：

- 关掉 Unity 编辑器后重新 `/build`；
- 或者走 CI：`gh workflow run build.yml -f targets=Windows`（用法见 `docs/ci-setup.md`）。

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

## 3. 汇报

**成功**：给产物路径与体积，一句话说完。默认产物是 `Builds/Windows/21Days.exe` 与 `Builds/Android/21Days.apk`。

```powershell
Get-ChildItem -Recurse Builds/Windows | Measure-Object -Property Length -Sum
```

例：`Builds/Windows/21Days.exe（连同 Data 目录共 182 MB）`。Android 则给 `.apk` 文件本身的大小。

**失败**：读日志的**尾部**（末尾 100～200 行足够）—— 文件名里的平台是小写，即
`Logs/build-windows.log` / `Logs/build-android.log`。挑出**前几条**真正的错误：

- 编译错误 → 给 `错误码 + 一句话 + 文件:行`（如 `CS0246: 找不到类型 FooService — Assets/_Project/Scripts/Runtime/Foo/Bar.cs:42`）；
- 构建期错误（缺场景、Android SDK/NDK 未配、签名、磁盘不足）→ 给那一行原文 + 一句中文解释 + 该去改哪儿。

**不贴整份日志**，也不要把同一个错误的十几行堆栈全搬过来。错误超过 5 条就报前 5 条并说明「还有 N 条同类」。
日志里如果出现本机路径，转述时替换成工程相对路径。

## 边界

- 只跑构建，**不改工程文件**。要改 Player Settings / 场景列表，先说明再单独做，不要顺手塞进这次打包。
- 不 `git add` / `git commit` 产物；`Builds/` 已在 `.gitignore` 里。
- 出给别人的正式版本走 CI 打 tag（`docs/ci-setup.md`），本机包只用于自测。

## 完成标准

编辑器开着时：已停下并给出两条路，没有任何重试。
编辑器没开时：构建进程已结束，且要么报出了产物路径与体积，要么给出了从日志里摘的前几条错误与对应文件行；卡在哪一步说不清就直说卡在哪，不猜结果。
