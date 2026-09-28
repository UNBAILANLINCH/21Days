---
type: extension-guide
module: mirror
layer: runtime
maturity: seed
---

# Mirror 扩展指南

## 加一只妖（场景内容 + 一行数据表，不用改代码）

1. 在 `Tables/Defines/yao.xml` 对应的数据目录 `Tables/Data/yao/` 下新增一个 `<id>.json`，字段齐全：`id`（全局唯一）、
   `disguise_name`、`true_name`、`true_desc`、`true_image`（Addressables 地址，需已导入并登记）、`flaw`、`obsession`、`clan`、
   `clue_items`（tbitem id 列表，空 = 一照就见）、`sealable`、`mask`（本波占位，先照抄现有两行的取值）。字段含义见
   `Assets/_Project/Scripts/Core/Config/Generated/yao/Yao.cs:43-83` 的注释。
2. Luban 生成后确认 `Assets/_Project/Data/Config/yao_tbyao.bytes` 有更新（内容不变则不重写文件，见 pitfalls）。
3. 场景里给这只妖挂 `MirrorSubject`（`kind = Yao`，`yaoId` 填新 id）；跟随巡逻怪的还要勾 `followsMonster`。
   走 Unity 编辑器 / Unity MCP 接线，不手改 YAML。
4. 需要真形图就把 Sprite 放进 `Mirror` 组（Addressables 地址与 `true_image` 一致，见 mirror-module-guide「Addressables 用 Mirror 组」）。
5. 这就是 PRD V12 的验收依据：新增一只妖只加数据表与场景标记，`MirrorRules` / `MirrorService` 都不用改
   （`MirrorRules.Resolve` 按 `MirrorCandidate.YaoId` 查 `HasClues` 委托，见 `Assets/_Project/Scripts/Runtime/Mirror/MirrorRules.cs:55-58`）。

## 加一种照镜结果（新增 `MirrorResultKind`）

当前结果种类固定五种 + 一种自照恒定值（`Assets/_Project/Scripts/Runtime/Mirror/MirrorResultKind.cs:8-27`）。要新增一种（例如「照见已收押的妖」）：

1. `MirrorResultKind` 加新枚举值；`MirrorRules.Resolve` 的 `switch`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorRules.cs:49-61`）按新条件返回它——判断逻辑本身仍是纯函数，
   EditMode 直接穷举新分支。
2. `MirrorResultInfo.Compose`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorResultInfo.cs:49-73`）的 `switch` 加一支，从 `MirrorConfig` 取新标题 / 说明文案；
   `MirrorConfig` 相应加 `[SerializeField] private string` 字段（同 `blurryTitle` / `nothingTitle` 的写法，`Assets/_Project/Scripts/Runtime/Mirror/MirrorConfig.cs:62-90`）。
3. 如果新结果要单独记存档（同 `Identified` / `GlimpsedBlurry`），在 `MirrorSaveData` 加字段并升版本号
   （`Version` 属性见 `Assets/_Project/Scripts/Runtime/Mirror/MirrorSaveData.cs:12`，迁移写在 `Migrate` 见
   `Assets/_Project/Scripts/Runtime/Mirror/MirrorSaveData.cs:26-29`），
   `MirrorRules.Record`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorRules.cs:94-112`）加对应分支。
4. `MirrorResultView.Apply`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorResultView.cs:84-102`）一般不用改：它只读 `MirrorResultInfo` 的通用字段（标题 / 名字 / 说明 / 图 / 是否压暗）；
   新结果如果要专属观感（不是「压暗」也不是「空镜面」）才需要在 `ShowImage`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorResultView.cs:105-113`）加分支。
5. 不要把新结果种类塞进 `MirrorSubjectKind`：那是「对象是什么」，不是「照镜结果是什么」，两者故意分开
   （`Assets/_Project/Scripts/Runtime/Mirror/MirrorResultKind.cs:1-3` 文件头注释）。

新增结果需要异步加载图片时，沿用 `MirrorInputPresenter.ComposeAsync` 返回内容与句柄、`ShowAsync` 接管所有权的路径
（`Assets/_Project/Scripts/Runtime/Mirror/MirrorInputPresenter.cs:226`、`:160`），并补取消 / 打开失败回归；不要在加载回调里直接覆盖呈现器的图片字段。

## 加一个通灵视条件

当前生效条件固定「雨 / 夜 / 昏暗任一为真」（`Assets/_Project/Scripts/Runtime/Mirror/SpiritSightRules.cs:15`）。要加新条件（例如「持有某道具」）：

1. `SpiritSightZone` 加一个 `[SerializeField] private bool` 字段（同 `rain`/`night`/`dim` 的写法，`Assets/_Project/Scripts/Runtime/Mirror/SpiritSightZone.cs:19-26`），
   或者如果条件与「区域」无关（比如「持有道具」是全局条件、不挂在某个区域上），改造 `SpiritSightRules.IsActive` 的签名，
   在 `SpiritSightPresenter.Tick`（`Assets/_Project/Scripts/Runtime/Mirror/SpiritSightPresenter.cs:66`）里把新判断项传进去。
2. `SpiritSightRules.IsActive`（`Assets/_Project/Scripts/Runtime/Mirror/SpiritSightRules.cs:15`）是纯函数，加一个 `bool` 参数、`||` 上新条件即可，EditMode 直接补用例。
3. 不要把新条件的判断逻辑写进 `SpiritSightPresenter` 或 `SpiritSightZone` 本身：规则必须留在 `SpiritSightRules` 里保持可脱离场景单测
   （同 `MirrorRules` / `MirrorCrackRules` 的分法，`Assets/_Project/Scripts/Runtime/Mirror/SpiritSightRules.cs:1-5` 文件头注释）。
4. 条件为「持有道具」一类需要跨模块读取时，走 `MirrorService` 或 `MirrorSceneBinder` 一样的路径：构造函数注入对应服务的只读接口
   （例如 `LootService.Items`），不要让 `SpiritSightZone`（场景组件）自己注入服务——场景组件只做标记，不读输入、不认识容器
   （同 `MirrorSubject` / `SpiritSightZone` 现在的边界）。

## 依赖方向约束

新扩展只能 `Game.Mirror → Game.Player` / `Game.Monster` / `Game.Loot` / `Game.Session` / `Game.Dialogue` / `Game.Core`（均只读）；
不要引用 `Editor` / `Tests` 程序集；不要让 Mirror 反向依赖任何读它的未来模块（收押、画皮）——那些模块应该只读
`MirrorService` 的公开方法（见 `mirror-external-api.md`），不应该被 Mirror 引用。场景组件（`MirrorSubject` / `SpiritSightZone`）
不注入服务，扩展逻辑放规则类或入口点里。

## 验证

规则改动配 EditMode 测试（`Assets/_Project/Scripts/Tests/EditMode/Mirror/`）；玩家可见行为（照镜结果、通灵视提示、镜裂 / 镜碎）走
`Assets/_Project/Scripts/Tests/Showcase/Mirror/MirrorShowcase.cs`。场景资产只通过 Unity 编辑器或 Unity MCP 修改；Luban 表改动后
跑一次生成，确认 `Tables.cs` 与 `yao_tbyao.bytes` 都同步。
