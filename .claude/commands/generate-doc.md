---
description: 从源码生成 / 同步模块文档三件套（知识层自动化）
argument-hint: [sync|check] <模块名>
---

# /generate-doc — 模块文档生成 / 同步

参数：**$ARGUMENTS**（`<模块>` | `sync <模块>` | `check <模块>`）

依据 `.claude/skills/generate-doc/modules.json` 注册表工作。先读该 skill 的 `SKILL.md`。

## 三种模式

| 用法 | 做什么 |
| --- | --- |
| `/generate-doc <模块>` | 从 `Assets/_Project/Scripts/Runtime/<Module>/` 源码**生成完整三件套**（模块首次落地时用） |
| `/generate-doc sync <模块>` | 对比代码与已有文档，**增量**更新变化处，保留人工补充的段落 |
| `/generate-doc check <模块>` | **只报告**文档与代码的差异，不改任何文件 |

## 步骤

1. 从 `.claude/skills/generate-doc/modules.json` 取该模块的源码路径与文档目录；模块未登记则先登记一条（源码 `Assets/_Project/Scripts/Runtime/<Module>/`，文档 `ai-docs/docs/modules/<模块小写>/`，状态 `seed`）。
2. 读源码，提取：
   - 公开类型与公开成员签名（`public class` / `public` 属性 / `public` 方法 / `event`）。
   - MonoBehaviour 的生命周期用法（`Awake` / `Start` / `OnEnable` 各做什么）。
   - ScriptableObject 配置类及其字段含义、对应资产在 `Assets/_Project/Data/<Module>/` 的位置。
   - asmdef 归属与依赖方向；跨模块引用了谁、被谁引用。
   - 序列化暴露面：Inspector 上需要拖赋的引用、需要调的数值。
3. 按三件套职责写 / 更新文档，产物落 `ai-docs/docs/modules/<模块小写>/`：

   | 文件 | 职责 |
   | --- | --- |
   | `<模块>-module-guide.md` | 编辑这个模块**之前必须知道的**：职责边界、内部结构、核心数据、生命周期、Inspector 接线要求、禁止事项 |
   | `<模块>-external-api.md` | 公开接口签名、调用约束、禁止事项（别的模块查这份） |
   | `<模块>-extension-guide.md` | 扩展点与扩展模式（要给这个模块加东西时看） |

   frontmatter 四个字段：

   ```yaml
   ---
   type: module-guide | external-api | extension-guide
   module: <模块>
   layer: runtime | editor
   maturity: seed | stable
   ---
   ```

4. 更新 `modules.json` 的状态，并在 `ai-docs/docs/catalog.md` 的模块表里补 / 改这一行。
5. 输出：改了哪些文档、关键变更点（新增 / 删除了哪些公开接口）。

## 注意

- **guide 聚焦「编辑前必须知道的」**，不堆实现细节——实现细节读代码更准；写成代码复读机就失去了价值。篇幅约束见 `ai-docs/docs/modules/README.md`。
- `external-api` 只列**公开**接口与调用约束，私有实现不写；写清楚「不要这样调」。
- `sync` 模式不要覆盖人工补写的段落（设计取舍、历史原因），只改被代码改动影响的部分。
- 当前模块表为空：第一个玩法模块落地后再生成。跑完顺手跑一次 `/gc` 确认没有失效链接。
