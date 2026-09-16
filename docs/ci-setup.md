# CI 接入（2026-09-16 已搁置）

> **结论先行**：GitHub Actions 这条路在本工程**没走通**，两个工作流（`build.yml`、`unity-tests.yml`）
> 已删除，三个 secret 已清空，仓库里不再有任何会自己跑起来的 CI。
> **当前唯一的出包与测试方式是本机**，见本文末尾和 [`developer-guide.md` 第 14 章](developer-guide.md)。
>
> 这份文档保留下来只有一个目的：**将来谁想重做，不用重踩一遍**。下面记的是验到哪一步、卡在哪、为什么。

## 卡在哪：Unity 账号登录返回 401

流水线本身没问题，卡在 game-ci 激活 Unity 许可证这一步。关键日志：

```
UnityConnectLoginRequest: Failed to login - please check your username or password
No sufficient permissions while processing request
  "https://core.cloud.unity3d.com/api/login", HTTP error code 401
  ↓
[Licensing::Module] Error: Access token is unavailable; failed to update
[Licensing::Module] Error: Failed to activate entitlement license
[Licensing::Module] Error: Failed to activate ULF license
```

`entitlement` 和 `ULF` 两条激活路径都试过，都断在同一个地方：**登录拿不到 access token**。
game-ci 自动重试 5 次，全失败，两个平台表现完全一致。

怀疑顺序（未逐一排除，留给下次）：

1. **账号开了两步验证（2FA）**——最可能。开了 2FA 的账号走不通 `/api/login` 这个接口，
   密码再正确也是 401，这是 game-ci 对 Personal 账号的已知薄弱点。
2. 密码粘贴有误（尾部空格 / 换行）。
3. 账号是第三方登录（Google 等）创建的，从来没设过独立密码。

自查方法：无痕窗口开 https://id.unity.com ，用同一套邮箱密码登录。能进 → 是 2FA；进不去 → 是凭证本身。

## 更本质的原因：Personal 许可证的模型和 CI 不兼容

**本机为什么不需要这一套**：激活在装 Unity 时就做完了，结果存在 `%PROGRAMDATA%\Unity\Unity_lic.ulf`，
此后本机跑编辑器或 batchmode 都只读这个本地文件，**完全不联网**。

**CI 为什么绕不开**：runner 每次是全新的临时机器，跑完即焚，没有任何激活痕迹，所以**每跑一次就要从零激活一次**。
而 `.ulf` 是**机器绑定**的（日志里能看到 `Machine Id: 0LK5DSp2HPgGnf+Nl86RGLDdrlw=`），
直接拷进容器签名校验过不了。game-ci 的办法是伪造一个随机 machine ID
（日志里的 `Randomizing machine ID for personal license activation`）再走**在线激活**——而在线激活必须先登录账号。

链条：容器无状态 → 每次要激活 → Personal 许可证激活要绑机器 → 绑机器要在线验证账号 → 401 → 断。

再往上一层：Unity Personal 的授权模型是「**一个账号最多绑 2 台机器**」，这是给人用的，不是给 CI 用的——
CI 每跑一次就是一台"新机器"。game-ci 那套随机 machine ID 本质上是在绕这个限制，所以对 Personal 用户
一直是「能跑通但很脆」的状态。Unity 官方给 CI 的正式答案是 **Licensing Server**（浮动许可证），
但那是 Pro / Enterprise 才有的东西。

## 验证到哪一步（这些环节当时是通的）

删掉的工作流并非写错了。2026-09-16 实测，除 Unity 激活外**全部环节验证通过**：

| 环节 | 结果 |
| --- | --- |
| `workflow_dispatch` 手动触发 | ✅ |
| 输入参数传递（`-f targets=...`） | ✅ |
| jq 解析平台列表 → 矩阵展开 | ✅ 注解输出 `本次打包平台：["Android"]` |
| 许可证门（没配 secret 时跳过而非失败） | ✅ 两条分支都验过 |
| 矩阵并行（Windows + Android 同时跑） | ✅ |
| 腾磁盘 / checkout / `Library` 缓存 | ✅ |
| 拉 Unity 镜像（`unityci/editor:ubuntu-2022.3.62f2-*`） | ✅ |
| **激活 Unity 许可证** | ❌ **401，卡在这里** |
| Unity 构建、产物上传、挂 Release | ⬜ 没验到，全在激活后面 |

失败的 run 留在仓库 Actions 历史里（`35054348697`、`35061914674`），日志可回溯。

## 将来想重做的话

前置条件是先解决账号认证，两条路：

- **注册一个专用的 CI Unity 账号**（不开 2FA），用它拿 `.ulf`。比关掉主账号的 2FA 干净，推荐这条。
- 或者升级到 Pro / Enterprise，用 `UNITY_LICENSING_SERVER`——正规方案，但要花钱。

工作流文件可以从 git 历史里捞回来（本次删除的提交里有完整的两份，写得是对的，直接复用）：

```powershell
git log --oneline --diff-filter=D -- .github/workflows/   # 找到删除那次提交
git show <commit>^:.github/workflows/build.yml > .github/workflows/build.yml
```

拿到 `.ulf` 的步骤（当时验证有效，留档）：

1. Unity Hub → **Preferences → Licenses → Add → Get a free personal license**。
2. Windows 下文件落在 `%PROGRAMDATA%\Unity\Unity_lic.ulf`。

> **Hub 里显示「已激活」不等于这个文件已经生成。** 新版 Hub 有时走在线 license 服务，本地没有 `.ulf`。
> 找不到就在 Hub 里把许可证 Return / 删掉再重新 Add 一次（选 personal license）。

> `.ulf` 是**凭证**，等价于账号授权：不要提交进仓库、不要贴进对话或聊天窗口、不要放进截图。
> 密码同理，`gh secret set UNITY_PASSWORD` 不带参数就是为了让它走交互输入、不留在命令历史里。
> **另外**：`gh secret set` 不校验空值，直接回车会设成空字符串还报绿勾 `✓`——本次就在这上面浪费了一轮，
> 邮箱这种非敏感值建议用 `--body` 显式传，设完 `gh secret list` 看时间戳确认。

## 现在怎么出包：本机

```powershell
powershell -File scripts/build.ps1 -Target Windows                 # 端游
powershell -File scripts/build.ps1 -Target Android -Version 0.1.0  # 手游
powershell -File scripts/build.ps1 -Target Windows -Development    # 带调试符号与 Profiler
```

参数：`-Target Windows|Android`（必填）、`-Version`、`-BuildNumber`（Android 的 versionCode）、
`-Development`、`-Release`（只对 Android 有效）、`-OutputPath`。
默认产物 `Builds/Windows/21Days.exe`、`Builds/Android/21Days.apk`（`Builds/` 已在 `.gitignore` 里），
日志在 `Logs/build-windows.log` / `Logs/build-android.log`。

Android 装机：`adb install -r Builds/Android/21Days.apk`。工程默认 IL2CPP + ARM64
（2026-09-16 起，起因是纯 64 位手机装不了 32 位包），约 268 秒出包，Unity 默认的调试签名装机够用。

> **打包时 Unity 编辑器必须关闭** —— 编辑器开着时工程被 `Temp/UnityLockfile` 锁住，批处理进不去。
> `scripts/build.ps1` 检测到编辑器在跑会直接以 **exit 2** 拒绝，不要重试、不要绕、不要删锁文件。
>
> Claude Code 里用 `/build [Windows|Android] [版本号]` 走同一条路（技能会先替你判断编辑器状态）。

## 上架相关的缺口（真要上架时再看）

- 出的是 **APK**，Google Play 对新应用要 **AAB**：要把 `EditorUserBuildSettings.buildAppBundle` 翻成 `true`
  （建议加个 `-appBundle` 开关，别写死）。
- 包**没有签名**（用的是 Unity 默认调试签名），只适合自己和小范围测试分发，商店不收。
- **iOS 没做**：需要 macOS 机器 + Xcode + 证书与描述文件。
