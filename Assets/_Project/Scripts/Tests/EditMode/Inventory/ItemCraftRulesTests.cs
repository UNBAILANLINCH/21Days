// 职责：ItemCraftRules / ItemRecipe 的 EditMode 回归——配方命中、缺料（缺的是哪件）、原料为空、
//   配方写坏、按产物 id 查配方、同一原料写两行按行相加、以及「判定不改背包」这条纯规则约束。
// 为什么新建：对应被测类一个测试类（unity-tests.md）；InventoryRulesTests 钉的是展示合并与筛选，
//   配方是另一件事，塞进去会把那份文件的主题冲散。
//   配方与原料都直接手搓 id（不读真实物品表）：被测的是「原料 id + 件数 → 产物」这条纯规则，
//   不依赖 tbitem 里有没有那几行（真实表的解析由 InventoryRulesTests 的 TbItem 用例覆盖）。

using System.Collections.Generic;
using Game.Inventory;
using NUnit.Framework;

namespace Game.Tests.EditMode.Inventory
{
    public sealed class ItemCraftRulesTests
    {
        // 与 xlsx 里新增的示例行对齐：1401 水痕、1102 湿皮 → 1701 进入废弃官署的钥匙（05_皮面具与道具.md:106 R7）。
        private const int WaterMark = 1401;
        private const int WetSkin = 1102;
        private const int BlankSkin = 1103;
        private const int DragonPill = 1501;
        private const int DragonMask = 1202;
        private const int GateKey = 1701;

        private ItemRecipe gateKeyRecipe;
        private List<ItemRecipe> recipes;

        [SetUp]
        public void SetUp()
        {
            gateKeyRecipe = new ItemRecipe("gate_key", new[] { new ItemStack(WaterMark, 1), new ItemStack(WetSkin, 1) }, GateKey);
            recipes = new List<ItemRecipe> { gateKeyRecipe };
        }

        [Test]
        public void TryCraft_WhenEveryIngredientIsEnough_ReturnsOkWithTheProduct()
        {
            var items = new Dictionary<int, int> { { WaterMark, 2 }, { WetSkin, 1 } };

            ItemCraftResult result = ItemCraftRules.TryCraft(items, gateKeyRecipe);

            Assert.That(result.Status, Is.EqualTo(ItemCraftStatus.Ok));
            Assert.That(result.IsOk, Is.True);
            Assert.That(result.Recipe, Is.SameAs(gateKeyRecipe));
            Assert.That(result.Product.ItemId, Is.EqualTo(GateKey));
            Assert.That(result.Product.Count, Is.EqualTo(1));
            Assert.That(result.MissingItemId, Is.Zero);
        }

        [Test]
        public void TryCraft_WhenAnIngredientIsMissing_ReturnsMissingIngredientWithItsId()
        {
            var items = new Dictionary<int, int> { { WaterMark, 3 } };

            ItemCraftResult result = ItemCraftRules.TryCraft(items, gateKeyRecipe);

            Assert.That(result.Status, Is.EqualTo(ItemCraftStatus.MissingIngredient));
            Assert.That(result.MissingItemId, Is.EqualTo(WetSkin), "要报出缺的是哪一件，调用方才能提示玩家");
            Assert.That(result.Product.IsValid, Is.False, "没合成就不该给出产物");
        }

        [Test]
        public void TryCraft_WhenAnIngredientCountIsShort_ReturnsMissingIngredient()
        {
            var items = new Dictionary<int, int> { { WaterMark, 1 }, { WetSkin, 1 } };
            var twoWaterMarks = new ItemRecipe("gate_key_x2", new[] { new ItemStack(WaterMark, 2), new ItemStack(WetSkin, 1) }, GateKey);

            ItemCraftResult result = ItemCraftRules.TryCraft(items, twoWaterMarks);

            Assert.That(result.Status, Is.EqualTo(ItemCraftStatus.MissingIngredient));
            Assert.That(result.MissingItemId, Is.EqualTo(WaterMark));
        }

        [Test]
        public void TryCraft_WhenRecipeHasNoIngredients_ReturnsEmptyIngredients()
        {
            var empty = new ItemRecipe("empty", new ItemStack[0], DragonMask);

            ItemCraftResult result = ItemCraftRules.TryCraft(new Dictionary<int, int> { { DragonPill, 9 } }, empty);

            Assert.That(result.Status, Is.EqualTo(ItemCraftStatus.EmptyIngredients));
            Assert.That(result.Recipe, Is.Null);
        }

        [Test]
        public void TryCraft_WhenIngredientsListIsNull_ReturnsEmptyIngredients()
        {
            var nullIngredients = new ItemRecipe("null", null, DragonMask);

            ItemCraftResult result = ItemCraftRules.TryCraft(new Dictionary<int, int>(), nullIngredients);

            Assert.That(result.Status, Is.EqualTo(ItemCraftStatus.EmptyIngredients));
        }

        [Test]
        public void TryCraft_WhenRecipeIsNull_ReturnsNoRecipe()
        {
            ItemCraftResult result = ItemCraftRules.TryCraft(new Dictionary<int, int>(), (ItemRecipe)null);

            Assert.That(result.Status, Is.EqualTo(ItemCraftStatus.NoRecipe));
        }

        [Test]
        public void TryCraft_WhenProductIsMalformed_ReturnsInvalidRecipe()
        {
            var badProduct = new ItemRecipe("bad_product", new[] { new ItemStack(WaterMark, 1) }, 0);

            ItemCraftResult result = ItemCraftRules.TryCraft(new Dictionary<int, int> { { WaterMark, 1 } }, badProduct);

            Assert.That(result.Status, Is.EqualTo(ItemCraftStatus.InvalidRecipe));
        }

        [Test]
        public void TryCraft_WhenAnIngredientCountIsNotPositive_ReturnsInvalidRecipe()
        {
            var badIngredient = new ItemRecipe("bad_ingredient", new[] { new ItemStack(WetSkin, 0) }, GateKey);

            ItemCraftResult result = ItemCraftRules.TryCraft(new Dictionary<int, int> { { WetSkin, 5 } }, badIngredient);

            Assert.That(result.Status, Is.EqualTo(ItemCraftStatus.InvalidRecipe));
        }

        [Test]
        public void TryCraft_WhenRecipeIdIsEmpty_ReturnsInvalidRecipe()
        {
            var nameless = new ItemRecipe(string.Empty, new[] { new ItemStack(WetSkin, 1) }, GateKey);

            ItemCraftResult result = ItemCraftRules.TryCraft(new Dictionary<int, int> { { WetSkin, 1 } }, nameless);

            Assert.That(result.Status, Is.EqualTo(ItemCraftStatus.InvalidRecipe));
        }

        [Test]
        public void TryCraft_WhenTheSameIngredientIsListedTwice_SumsItsCount()
        {
            var doubled = new ItemRecipe("doubled", new[] { new ItemStack(WaterMark, 1), new ItemStack(WaterMark, 2) }, GateKey);

            ItemCraftResult short_ = ItemCraftRules.TryCraft(new Dictionary<int, int> { { WaterMark, 2 } }, doubled);
            ItemCraftResult enough = ItemCraftRules.TryCraft(new Dictionary<int, int> { { WaterMark, 3 } }, doubled);

            Assert.That(short_.Status, Is.EqualTo(ItemCraftStatus.MissingIngredient), "两行相加要 3 件，只有 2 件");
            Assert.That(enough.Status, Is.EqualTo(ItemCraftStatus.Ok));
        }

        [Test]
        public void TryCraft_ByProductId_WhenRecipeExists_FindsIt()
        {
            var items = new Dictionary<int, int> { { WaterMark, 1 }, { WetSkin, 1 } };

            ItemCraftResult result = ItemCraftRules.TryCraft(items, recipes, GateKey);

            Assert.That(result.Status, Is.EqualTo(ItemCraftStatus.Ok));
            Assert.That(result.Recipe, Is.SameAs(gateKeyRecipe));
        }

        [Test]
        public void TryCraft_ByProductId_WhenNoRecipeMatches_ReturnsNoRecipe()
        {
            var items = new Dictionary<int, int> { { WaterMark, 1 }, { WetSkin, 1 }, { DragonPill, 1 } };

            Assert.That(ItemCraftRules.TryCraft(items, recipes, DragonMask).Status, Is.EqualTo(ItemCraftStatus.NoRecipe), "产物没配方");
            Assert.That(ItemCraftRules.TryCraft(items, recipes, 0).Status, Is.EqualTo(ItemCraftStatus.NoRecipe), "产物 id 非正");
            Assert.That(ItemCraftRules.TryCraft(items, new List<ItemRecipe>(), GateKey).Status, Is.EqualTo(ItemCraftStatus.NoRecipe), "配方表为空");
            Assert.That(ItemCraftRules.TryCraft(items, null, GateKey).Status, Is.EqualTo(ItemCraftStatus.NoRecipe), "配方表为 null");
        }

        [Test]
        public void TryCraft_WhenTheBackpackDictionaryIsNull_ReturnsMissingIngredient()
        {
            ItemCraftResult result = ItemCraftRules.TryCraft(null, gateKeyRecipe);

            Assert.That(result.Status, Is.EqualTo(ItemCraftStatus.MissingIngredient));
            Assert.That(result.MissingItemId, Is.EqualTo(WaterMark));
        }

        [Test]
        public void TryCraft_DoesNotTouchTheBackpackDictionary()
        {
            var items = new Dictionary<int, int> { { WaterMark, 4 }, { WetSkin, 2 } };

            ItemCraftResult result = ItemCraftRules.TryCraft(items, gateKeyRecipe);

            Assert.That(result.Status, Is.EqualTo(ItemCraftStatus.Ok));
            Assert.That(items[WaterMark], Is.EqualTo(4), "判定不改背包：扣料归调用方（ItemCraftRules 文件头）");
            Assert.That(items[WetSkin], Is.EqualTo(2));
            Assert.That(items.Count, Is.EqualTo(2), "也不该凭空加进产物");
        }

        [Test]
        public void Recipe_WhenTheSourceListChangesAfterwards_KeepsItsOwnCopy()
        {
            var source = new List<ItemStack> { new ItemStack(WaterMark, 1) };
            var recipe = new ItemRecipe("copy", source, GateKey);

            source.Add(new ItemStack(DragonPill, 5));
            source[0] = new ItemStack(WetSkin, 9);

            Assert.That(recipe.Ingredients.Count, Is.EqualTo(1), "配方要复制原料，不能跟着外部列表变");
            Assert.That(recipe.Ingredients[0].ItemId, Is.EqualTo(WaterMark));
            Assert.That(recipe.RequiredCount(WaterMark), Is.EqualTo(1));
            Assert.That(recipe.RequiredCount(DragonPill), Is.Zero);
        }

        [Test]
        public void Recipe_WithTwoIngredientsAndAProduct_IsValid()
        {
            Assert.That(gateKeyRecipe.IsValid, Is.True);
            Assert.That(gateKeyRecipe.Id, Is.EqualTo("gate_key"));
            Assert.That(gateKeyRecipe.ProductId, Is.EqualTo(GateKey));
            Assert.That(gateKeyRecipe.ProductCount, Is.EqualTo(1));
        }

        [Test]
        public void ItemStack_WithNonPositiveIdOrCount_IsNotValid()
        {
            Assert.That(new ItemStack(BlankSkin, 1).IsValid, Is.True);
            Assert.That(new ItemStack(0, 1).IsValid, Is.False);
            Assert.That(new ItemStack(BlankSkin, 0).IsValid, Is.False);
            Assert.That(new ItemStack(BlankSkin, -1).IsValid, Is.False);
        }
    }
}
