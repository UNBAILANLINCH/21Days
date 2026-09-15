# CI 接入（一次性）

两条 GitHub Actions 流水线：**push 跑 EditMode 测试**、**打 tag 出包**（Windows 端游 + Android 手游，共用一套内容）。
配置文件在 `.github/workflows/`，本文只讲**一次性要做什么**以及**平时怎么用**。

## 为什么是 GitHub Actions + game-ci

- 本仓库是公开仓库，GitHub 托管的标准 runner **分钟数免费**，不用自己搭机器。
- Unity 官方没给现成的 Action，社区事实标准是 [game-ci](https://game.ci)：`unity-test-runner` 跑测试、`unity-builder` 出包，
  底层是 Docker Hub 上按 Unity 版本发布的编辑器镜像（`2022.3.62f2` 的 base / android / windows-il2cpp 等标签都有）。
- 两个 Action 都支持 `unityVersion: auto`，会去读 `ProjectSettings/ProjectVersion.txt`，**升 Unity 版本时工作流不用改**。

## 一次性：配三个 secret

Unity Personal 许可证在 Actions 里要三样东西：许可证文件全文 `UNITY_LICENSE`、账号 `UNITY_EMAIL`、密码 `UNITY_PASSWORD`。

### 1. 拿到 `.ulf` 许可证文件

1. 打开 Unity Hub → **Preferences → Licenses → Add → Get a free personal license**。
2. Windows 下文件落在：

   ```
   %PROGRAMDATA%\Unity\Unity_lic.ulf
   ```

   资源管理器地址栏直接粘 `%PROGRAMDATA%\Unity` 就能进去。

> **Hub 里显示「已激活」不等于这个文件已经生成。** 新版 Hub 有时走的是在线 license 服务，本地没有 `.ulf`。
> 找不到文件就在 Hub 里把许可证 **Return / 删掉再重新 Add 一次**（选 personal license），然后回到这个目录确认文件出现了。

### 2. 写进仓库 secret

在工程目录下跑（`gh` 是 GitHub CLI，没装先 `winget install GitHub.cli` 并 `gh auth login`）：

```powershell
gh secret set UNITY_LICENSE < "$env:PROGRAMDATA\Unity\Unity_lic.ulf"
gh secret set UNITY_EMAIL      # 回车后交互输入 Unity 账号邮箱
gh secret set UNITY_PASSWORD   # 回车后交互输入密码
```

`gh secret list` 能看到三条就算好了。

> `.ulf` 是**凭证**，等价于账号授权：**不要提交进仓库**、不要贴进对话或聊天窗口、不要放进任何截图。
> 只通过 `gh secret set` 或 GitHub 网页 Settings → Secrets 的输入框交给 Actions。
> 密码同理 —— 上面两条命令故意不带参数，就是为了让密码走交互输入，不留在命令历史里。

### 3. 没配 secret 时会怎样

两个工作流第一个 job 都是 `check-license`：探测 `UNITY_LICENSE` 是否为空，把结果写成 job 输出，
后续 job 用 `if: needs.check-license.outputs.has_license == 'true'` 判断。

所以**没配 secret 时流水线是跳过，不是失败** —— Actions 页面上显示为绿色（跳过的 job 灰掉），
日志里有一条 `::notice::` 指回本文。刚克隆仓库、或别人 fork 过去，都不会看到一片红叉。

（为什么要绕这一圈：GitHub 不允许在 **job 级的 `if`** 里读 `secrets` 上下文，只能先在 step 里读，再经 job 输出传出去。）

## 平时怎么触发

| 想做什么 | 怎么做 |
| --- | --- |
| 跑测试 | push 到 `main` 或开 PR 自动跑；也可以在 Actions 页点 Run workflow |
| 出正式包 | `git tag v0.1.0 && git push --tags` —— 两个平台都打，产物自动挂到该 tag 的 Release |
| 只打某个平台试一下 | `gh workflow run build.yml -f targets=Android` |
| 手动出包并指定版本号 | `gh workflow run build.yml -f targets=StandaloneWindows64,Android -f version=0.2.0` |
| 看进度 / 取产物 | `gh run list`、`gh run watch`；产物在 run 页面的 Artifacts，或 tag 对应的 Release |

版本号三选一（工作流里已写死这个优先级）：tag 触发用 tag 名（`versioning: Tag`）→ 手动触发填了 `version` 用它（`Custom`）→
都没有就沿用 `ProjectSettings` 里的版本（`None`，不动工程文件）。

## 时间预期

| 场景 | 大概耗时 |
| --- | --- |
| 首次构建（`Library` 冷缓存，要完整导入资产） | **15～30 分钟**，Android 通常更久 |
| 缓存命中后的增量构建 | 5～10 分钟 |
| EditMode 测试（缓存命中） | 3～6 分钟 |

`Library` 用 `actions/cache@v4` 缓存，key 跟着 `Packages/manifest.json` 与 `ProjectVersion.txt` 走 ——
**改包依赖或升 Unity 版本就会冷一次**，属于正常现象，别当成故障。
GitHub 的缓存单仓库上限 10 GB，超了自动淘汰最旧的。

## 已知的缺口（别忘了）

- **Android 默认是 Mono + ARMv7/ARM64 的调试包**，装机自测够用，**上架 Google Play 前必须切 IL2CPP + 仅 ARM64**
  （Play 从 2019 年起要求 64 位）。切法：在 `BuildScript.cs` 里设 `PlayerSettings.SetScriptingBackend` /
  `targetArchitectures`，CI 侧则在 `unity-builder` 的参数里加对应设置；顺带还要配签名 keystore（再加 3 个 secret）。
- 出的是 **APK**（`androidExportType: androidPackage`），装机即用；Google Play 要的 **AAB** 是另一个导出类型，上架时再切。
- **iOS 没做**：需要 macOS runner（分钟数按 10 倍计费）+ Xcode + 证书与描述文件，属于第三阶段。
- Release 里的包**没有签名**，只适合自己和小范围测试分发。

## 本机打包与 CI 的关系

两条路**共用 `EditorBuildSettings` 里的场景列表**，所以场景增删只在编辑器里改一次，两边都生效。区别只在入口：

| | 入口 | 什么时候用 |
| --- | --- | --- |
| 本机 | `scripts/build.ps1` → `Game.Editor.BuildScript.BuildWindows` / `BuildAndroid` | 改了东西想马上装机看效果 |
| CI | `game-ci/unity-builder` 自带的构建入口 | 出给别人的版本、打 tag 归档、不想占着本机 |

本机打包：

```powershell
powershell -File scripts/build.ps1 -Target Windows           # 端游
powershell -File scripts/build.ps1 -Target Android -Version 0.1.0
powershell -File scripts/build.ps1 -Target Windows -Development   # 带调试符号与 Profiler
```

参数：`-Target Windows|Android`（必填）、`-Version`、`-BuildNumber`（Android 的 versionCode）、
`-Development`（开关）、`-OutputPath`（改产物路径）。
默认产物 `Builds/Windows/21Days.exe`、`Builds/Android/21Days.apk`（`Builds/` 已在 `.gitignore` 里），
日志在 `Logs/build-windows.log` / `Logs/build-android.log`。

> **打包时 Unity 编辑器必须关闭** —— 编辑器开着时工程被锁，批处理进不去。
> `scripts/build.ps1` 检测到编辑器在跑会直接以 **exit 2** 拒绝，不要重试、不要绕。
> 不想关编辑器就走 CI：`gh workflow run build.yml -f targets=Windows`。
>
> Claude Code 里用 `/build [Windows|Android] [版本号]` 走同一条路（技能会先替你判断编辑器状态）。
