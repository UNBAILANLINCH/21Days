# 五店主场景落地范围

2026-10-13：用户已批准执行文档及按店分组显隐，new-feature设计门已满足，不重复请求批准。

本次在正常Boot进入的既有3D灰盒地图预摆五个建筑Cube和现成小人，各店可独立完成；场景内切人、移动、下落音游和回合制继续复用已有通路。

实际产物、验证状态与待审清单见[主场景实施记录](main-implementation-report.md)。

验收：
1. 正常Boot开始/继续进入原地图，五个建筑与现成小人可见，无独立店铺场景加载。
2. 进入/离开店前范围正确显隐，控制人物出店不消失，隐藏不清进度；旧档原人物保持、新人物补初态。
3. 衣肆唯一领取、打造额度/四点/取消/提交、客栈对白各从现场交互完成。
4. 戏班验身/换路线/现有四轨音游完成及失败返回；酒肆按角色灌酒、移动倍率、现有战斗继承与离区清零。
5. 编译、相关EditMode与正常Boot主场景Showcase通过，资产引用及.meta完整；人工画面验收单独记录。

修改范围：
- Scripts/Runtime/ShopPlaytest/既有接线改为同图组合；不再注册独立入口、独立身份或第二个模拟步骤。
- Scripts/Runtime/Taming/、Monster/补友善人物、选择资格、旧档扩展人物和现有移动倍率/路线接口。
- Scripts/Runtime/Interaction/补当前控制人物锚点，继续向Dialogue等读方发变化通知。
- Scripts/Runtime/Tavern/普通客人按稳定ID分开累计。
- Scripts/Editor/ShopPlaytest/既有生成器改为主地图预摆与Boot接线工具。
- Prefabs/ShopPlaytest/新增五店街面预制体；Prefabs/UI/局部面板收掉选店传送。
- Data/Identity/追加五店职业，Data/ShopPlaytest/沿用已确认玩法参数。
- Scenes/Boot.unity及Assets/Scenes/SampleScene.unity经Unity资产API接线；旧ShopPlaytest三个场景文件保留，地址加Legacy前缀，正常入口不使用。
- Scripts/Runtime/Session/GameSession.cs修正切槽期间旧场景离场捕获，相关EditMode与跨槽主场景回放验证。
- Scripts/Tests/EditMode/补显隐、锚点、旧档、友善和按角色醉酒边界；Scripts/Tests/Showcase/ShopPlaytest/及四店回放改为正常Boot。
- 模块说明、公开接口和PRP实施记录同步实际验证。

上述资产/脚本路径均相对Assets/_Project/，仅既有模板舞台SampleScene在Assets/Scenes/原位修改。
