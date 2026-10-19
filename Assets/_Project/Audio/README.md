# Audio — 音乐与音效

音乐和音效统一放在 `Assets/_Project/Audio/`，沿用现有大写目录，不另建 `Assets/_Project/audio/` 或项目根目录的 `audio/`。

## 文档入口

| 要做什么 | 对应文档 |
| --- | --- |
| 导入音频、设置格式、检查 BGM 循环 | [美术手册第 8 章：音频](../../../docs/artist-guide.md#8-音频) |
| 了解音游入口、曲目与谱面时间规则 | [Rhythm 模块指南](../../../ai-docs/docs/modules/rhythm/rhythm-module-guide.md) |
| 试听、定位音符、调整候选谱面并导出 | [音频与谱面对照工具](../../../PRP/rhythm-audio-review-20261003/locator/README.md) |
| 程序接入 BGM / 音效服务 | [开发手册第 12 章：音频](../../../docs/developer-guide.md#12-音频) |

## 交付位置与说明

- 音游歌曲沿用现有 `Rhythm/`，目前已有三首测试曲；新曲放入这里后仍需程序配置曲库与谱面，放入文件不等于已接入游戏。
- 普通背景音乐与音效沿用现有 `bgm/`、`sfx/`，不另建仅大小写不同的目录。
- 交付时在同目录附一份说明，列出文件名、用途或触发场景、版本、作者与授权范围、时长，以及是否循环和循环起止秒数。
- 音游歌曲另列使用片段的起止秒数、已确认的 BPM / 拍号 / 首拍位置；不确定的项目写“待确认”。替换已制谱歌曲时说明裁剪、前导静音或时长是否变化，由程序复核谱面对齐。

## 导入与接入

**放进来会自动发生什么**：**没有自动导入规则**（只有图片有）。
2026-10-18 接收的 23 个 SFX 已通过 Unity 设置为 Decompress On Load、ADPCM、预加载、保留原采样率与立体声、不后台加载；这是本批设置，不自动应用到 BGM 或音游歌曲。
框架 BGM / SFX 服务按 Addressables 地址加载，由程序登记地址与调用；现有音游曲目由配置资产直接引用 AudioClip，不要求每首歌曲单独登记 Addressables。音乐同学交音频与说明，程序负责导入设置、资源接线和游戏内验证。

**命名**：全小写 + 下划线，`bgm_标题.ogg`、`sfx_ui_click.wav`。不用空格、不用中文。
文件名和 Addressables 地址不必相同，地址由程序定。

**交源文件**：优先提供无损 WAV 母版；MP3 / OGG 可作为试听或游戏压缩版本，不能当作无损母版（`.wav` / `.mp3` / `.ogg` / `.aif` / `.aiff`
在 `.gitattributes` 里都按二进制正确处理）。
BGM 要**能无缝循环**、头尾静音修干净——框架换曲是先淡出旧的再淡入新的，各 0.5 秒。
音效池只有 8 个声部，几秒长的声音别当普通音效用。

**不要在资源管理器里改名 / 移动 / 删除**——一律在 Unity 的 Project 窗口里操作，否则 `.meta` 掉队、引用静默失效。

游戏内试听、循环接缝和音游谱面对齐需分别确认；仅导入成功不代表验收通过。

## 2026-10-18 音效接收与资源登记

按本轮用户确认，本次只入库、登记资源并写交接，不接玩法播放。四批交付共 23 个 WAV，接收前按 SHA-256 检查整个 `Assets/` 内的音频，均未发现相同内容；现已全部复制到 `sfx/`，与交付文件逐字节一致。原文件保留，项目内统一小写与下划线，`&` 改为 `_and_`，源名 `possesion` 拼写统一为 `possession`。

需求表原样归档至 [聚光灯音效需求_T0-T3.xlsx](../../../docs/design/spotlight/source/聚光灯音效需求_T0-T3.xlsx)。它是需求参考，含 124 个事件（T0 19、T0–T2 81），不是 Luban 或运行时配置，不导入 `Tables/Data/`；表内旧版附身与后续阶段条目也不改变当前玩法定案。

全部资源已登记到现有 Addressables `Default Local Group`。下表“项目文件”相对于 `sfx/`；加载地址统一为 `SFX/` 加文件名去掉 `sfx_` 和 `.wav`，例如 `sfx_aware.wav` 对应 `SFX/aware`。10-18 只登记资源；10-19 的播放接线见下一节，下表保留接收时的用途判断。

用途对应仅依据需求表与交付文件名；候选项尚未经过听感确认，不计为需求验收完成。时长取 WAV 帧数，单位秒。

| 批次 | 交付原名 | 项目文件 | 秒 | 需求对应与下一步 |
| --- | --- | --- | ---: | --- |
| 10-12 | SFX_action-denied.wav | sfx_action_denied.wav | 0.400 | T0 FX-008 行动拒绝；明确拒绝后播，连点限频 |
| 10-12 | SFX_aware.wav | sfx_aware.wav | 0.600 | T0 FX-001 开始警戒；每次进入状态播一次 |
| 10-12 | SFX_chase-start.wav | sfx_chase_start.wav | 0.800 | T0 FX-002 敌对／追逐开始 |
| 10-12 | SFX_exposed.wav | sfx_exposed.wav | 1.600 | T0 FX-007 身份暴露；长于表内建议，待试听 |
| 10-12 | SFX_keypoint-found.wav | sfx_keypoint_found.wav | 0.800 | T0 FX-009 关键奖励／线索；确认进度写入后播 |
| 10-12 | SFX_possesion-detach.wav | sfx_possession_detach.wav | 1.600 | T0 FX-006 身份解除／附身退出；长于建议，待试听 |
| 10-12 | SFX_possesion-succeed.wav | sfx_possession_succeed.wav | 1.600 | T0 FX-005 附身成功；不自动当作 FX-004 本体换皮生效 |
| 10-13 | SFX_attack-stab-behind.wav | sfx_attack_stab_behind.wav | 0.600 | 背刺候选；可评估 FX-010 命中／FX-046 穿刺层，不能据文件名覆盖所有攻击 |
| 10-13 | SFX_been-attacked.wav | sfx_been_attacked.wav | 0.400 | T0 FX-011 玩家受击；实际受伤后播并限频 |
| 10-13 | SFX_execute-succeed.wav | sfx_execute_succeed.wav | 0.800 | T0 FX-014 处决成功；避免与背刺重复叠音 |
| 10-13 | SFX_knocked-down.wav | sfx_knocked_down.wav | 2.400 | T0 FX-012 玩家击倒；明显长于 0.3–0.8 秒建议，待试听 |
| 10-16 | SFX_lowkey-intense-kicks-faster.wav | sfx_lowkey_intense_kicks_faster.wav | 3.310 | T0 FX-003 危险持续声的较快节奏候选；独立启停、互斥与循环接缝待实现／验收 |
| 10-16 | SFX_lowkey-intense-kicks.wav | sfx_lowkey_intense_kicks.wav | 3.692 | T0 FX-003 危险持续声候选；不能连续调用一次性 SFX 模拟无限循环 |
| 10-16 | SFX_metal-hit-ground-and-scratch.wav | sfx_metal_hit_ground_and_scratch.wav | 2.483 | 金属落地拖擦候选；不直接视为 FX-019 有效锻打，需核对音色与接触点 |
| 10-16 | SFX_mirror-resonance&stop.wav | sfx_mirror_resonance_and_stop.wav | 4.966 | T0 FX-016 穿镜组合候选的共鸣／收尾层；需与转场定时同步 |
| 10-16 | SFX_mirror-vibrate.wav | sfx_mirror_vibrate.wav | 6.621 | T0 FX-016 穿镜组合候选的震动层；不把文件时长当作既定转场时长 |
| 10-16 | SFX_wind-loop.wav | sfx_wind_loop.wav | 1.655 | 穿镜风循环候选；FX-017 环境声完整性未满足，不能把短风循环当整套坊市音景 |
| 10-16 | SFX_wind-start.wav | sfx_wind_start.wav | 3.310 | T0 FX-016 穿镜风起候选；与镜面层组合时需控制叠音 |
| 10-16 | SFX_wind-stop.wav | sfx_wind_stop.wav | 4.966 | T0 FX-016 穿镜风停候选；取消／离场时需正确收尾 |
| 10-17 | SFX_runsteps.wav | sfx_runsteps.wav | 0.968 | T1 FX-027 奔跑脚步；需核对一段内落脚次数后决定触发间隔 |
| 10-17 | SFX_silence-steps.wav | sfx_silence_steps.wav | 1.000 | T1 FX-028 静步脚步；只跟随实际移动，不改变敌人听觉规则 |
| 10-17 | SFX_UI-select.wav | sfx_ui_select.wav | 0.500 | T1 FX-020 点击／FX-022 焦点移动候选；先试听确定用途，避免一次输入播两次 |
| 10-17 | SFX_walksteps.wav | sfx_walksteps.wav | 0.923 | T1 FX-026 行走脚步；静止、暂停、对白与离场须停 |

## 2026-10-19 玩法音效接线

Boot 的 `GameplayAudioInstaller` 注册 `Game.Audio.GameplayAudio`，作为启动服务预加载 22 个已有地址，并在表现帧消费真实玩法通知；纯规则只发布变化，音频不写存档、不改变回放格式。直接播放不经 Boot 的独立原型仍不接本服务。

| 已接入 | 触发与收尾 |
| --- | --- |
| FX-001／002 | 可用且未被驯服的敌人首次转警戒／敌对；友善及酒肆观测 NPC 不触发。敌对退回警戒不重播初次警戒音 |
| FX-003 | 存在敌对追兵时循环；奔跑逃离时切较快版本，两版本互斥；暂停挂起，脱战、死亡、升级回合制、离场及恢复现场停止 |
| FX-005／006／007 | 附身来源的身份借入、操控目标切换、主动／超时退出和真实露馅；读档不伪造借入／露馅事件；FX-004 暂无素材，不以附身音替代 |
| FX-008 | 身份借入被拒与处决明确被拒；F 让位给正常交互时静默；同音效限频 |
| FX-009 | 开箱、掉落、物品发放成功事件后播；一批多件同帧合并，不在读档重建背包时播 |
| FX-010／011／012／013／014 | 真实常规命中、玩家受击、击倒／死亡、处决成功；FX-013 按交付表复用 FX-012。拒伤不播命中；处决只播成功音，不叠常规命中音；同一次致死通知限频去重 |
| FX-016 | 正式人界穿镜开始叠风起／风循环／镜震，成功切共鸣及风停；失败／取消释放在途声部；不修改转场时长 |
| FX-020 候选 | 已打开面板中现有 Button 的实际点击／提交播 `ui_select`；不另播焦点移动音，避免一次输入双响。面板打开后才动态新增的按钮未统一覆盖 |
| FX-026／027／028 | 玩家当前操控锚点实际位移时循环行走／奔跑／潜行脚步；静止、暂停、切目标、瞬移和离场停止。使用原整段脚步，不切分落脚采样 |

金属落地拖擦 `metal_hit_ground_and_scratch` 继续保留为 FX-019 候选，未把它当成已经确认的锻打音。风循环用于穿镜，不代表已补齐 FX-017 坊市环境音景；胜利和音乐解谜点击仍无对应交付。

框架增加 `ISfxVoiceService.PlayVoice(clip, loop, volume)` 返回 `SfxVoice`；调用方先 `Dispose` 声部再释放 clip，`SetPaused` 成对暂停／继续，音量跟随 Master／Sfx，服务退出兜底停止。短事件沿用 `IAudioService.PlaySfx`。`AudioConfig` 新增同音效最短重触发间隔、危险声与脚步相对音量，旧资产使用字段默认值。

验证：Player／Monster／Identity／Stealth／Interaction 等定向 EditMode 回归 628/628；音频预加载、重触发限频、取消与释放专项 2/2；真实 AudioSource 暂停／继续、音量跟随和幂等停止 PlayMode 专项 1/1。Boot 实际启动日志已读回 22 个 SFX 加载及 GameplayAudio 初始化完成；Boot 场景通过编辑器挂接独立 Installer。未进行人工混音／听感、完整玩法回放及独立 Player 包验收。健康扫描只剩两份已有 Dynamic 字体缓存体积问题，本次未清字体数据。

### 接收时的交接边界（10-18 历史记录）

- 普通短事件可复用 `IAudioService.PlaySfxAsync("SFX/aware", volume, ct)`；声音先与真实成功、拒绝或状态变化对齐，再接调用。`AudioConfig` 只有混音器、声部数与 BGM 淡化参数，没有逐事件音效槽位，不能只在该资产上拖文件完成接线。
- 持续危险声、脚步和风循环需要可控生命周期；现有 SFX 是一次性播放，加载取消令牌不会停止已开始的声音。先实现暂停／退出／取消／切场清理与重复触发限制，不能占用唯一 BGM 通道来代替并行环境循环。
- T0 中 FX-004 换皮生效、FX-013 失败／死亡、FX-015 胜利、FX-018 音乐解谜正确点击尚无明确对应交付。FX-003／010／016／017／019 仍是上述候选或组合，不能计为已完成；其余 T1／T2 不因文件可复用而自动标齐。需求表早于本轮五店定案，实际事件以当前玩法为准。
- 作者、授权范围、版本号、循环起止与无缝性尚未随本批提供，均待补充；本次保留母版，不裁剪、不改音色。已回读验证 23 个有效 AudioClip、23 个唯一地址及全部导入参数；控制台未检到错误或警告。未做游戏内试听、循环接缝及 Player 加载验收，不将编辑器导入成功等同于声音验收。
