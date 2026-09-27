# Characters/Ark — 占位序列帧小人（明日方舟基建小人）

> **版权声明**：本目录全部图片是《明日方舟》基建小人，**版权归上海鹰角网络（Hypergryph）所有**，
> **仅作开发期占位**。**正式包体、宣传物料不得包含本目录的任何内容**；正式美术到位后必须整体替换。
>
> **有机械闸门**：Release 出包（不带 `-Development`）时 `PlaceholderAssetGuard` 会查进包场景与 Addressables 条目的依赖，
> 引用到本目录任何文件就直接失败并列出引用链；开发版只警告。不出包也能查：菜单 **21Days → 打包 → 检查占位素材引用**。

- **来源**：模型取自开源仓库 [isHarryh/Ark-Models](https://github.com/isHarryh/Ark-Models)，
  经仓库内 `scripts/ark-spine-frames/`（Spine → 透明 PNG 离线渲染）渲成序列帧：
  `render_frames.py --anims Relax,Move --max-height 384`，只保留待机（idle）与走路（walk）。
- **目录**：一个角色一个子目录 `<名字>/`，内含 `chr_<名字>_<状态>_<NN>.png` 与 `meta.json`（帧率、画布、脚底锚点）。

| 子目录 | 角色 | 场景里替换了谁 |
| --- | --- | --- |
| `amiya/` | 阿米娅 | 玩家 |
| `chen/` | 陈 | 巡逻怪 |
| `skadi/` | 斯卡蒂 | NPC `Npc_Elder` |
| `texas/` | 德克萨斯 | NPC `Npc_Traveler` |
| `exusiai/` | 能天使 | NPC `Npc_Villager` |

**没有跑步帧**：五个角色目前只有 idle 与 walk 两套帧，因为方舟源 Spine 模型本身没有跑步动画，渲不出 run 帧，不是筛选取舍。
按住奔跑时控制器的 Run 态复用 walk 剪辑，播放速率提到上限 1.6 倍（速率 = 地速 5 ÷ walk 剪辑地速 3 ≈ 1.67，夹到 1.6），
效果是「走得快」——腿摆快一点，但没有前倾、摆臂与步幅变化；这是占位素材的极限，不是代码问题。换正式美术时补一套 `run` 帧即可。

**替换方式**：把同名目录里的图换成美术交的序列帧（命名与规范见 `docs/artist-guide.md`「角色序列帧交付规范」），
然后菜单 **21Days → 角色 → 从序列帧生成小人…** 选该目录重跑生成工具。动画、控制器、预制体原地更新（GUID 不变），
场景里的引用不用重接。换成正式美术后建议改到 `Characters/<名字>/` 并删掉本目录。
