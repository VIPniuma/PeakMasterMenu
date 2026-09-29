using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace PeakMasterMenu.UI.Tabs;

/// <summary>
/// 物品页：手持物品的快捷操作 + 全物品生成器。
/// 生成器按「食品 / 工具 / 装备 / 神秘 / 其它」分类，用正方形图标网格展示。
/// </summary>
internal sealed class ItemsTab : MenuTab
{
    private static readonly string[] CategoryNames =
    {
        "收藏",
        "全部",
        "食品",
        "工具",
        "装备",
        "神秘",
        "其它",
    };

    // 网格：面板内容区约 1030px 宽，6 列刚好铺满。
    private const int Columns = 6;
    private const float CellSize = 162f;
    private const float CellSpacing = 10f;
    private const int MaxDisplayed = 240;

    private RectTransform itemList = null!;
    private TextMeshProUGUI itemHint = null!;
    private TextMeshProUGUI mobHint = null!;
    private TextMeshProUGUI targetText = null!;

    private int categoryIndex;
    private string searchText = string.Empty;
    private string builtKey = string.Empty;

    private bool targetAll;
    private Character? targetCharacter;

    internal override string Title => "物品";

    protected override void Build()
    {
        UiFactory.Section(Content, "手持物品");
        UiFactory.Switch(
            Content,
            "手持物品不消耗（次数 / 燃料不减）",
            Cheats.NoItemConsume,
            value => Cheats.NoItemConsume = value);
        UiFactory.Switch(Content, "丢弃时轻放在脚边", Cheats.GentleDrop, value => Cheats.GentleDrop = value);
        UiFactory.ActionButton(Content, "修复手持物品（回满次数 / 燃料）", RepairHeldItem);
        UiFactory.ActionButton(Content, "充满手持物品能量（燃料 / 电量）", RefillHeldItem);
        UiFactory.ActionButton(Content, "瞬间完成手持物品的烹饪", FinishCookingHeldItem);
        UiFactory.ActionButton(Content, "丢弃全部物品", DropAllItems);

        UiFactory.Section(Content, "发放目标");
        targetText = UiFactory.Hint(Content, "当前发放目标：自己");
        UiFactory.ActionButton(Content, "切换发放目标", CycleTarget);
        UiFactory.Hint(Content, "点物品格子就会把物品直接放进目标手里；选「房间内所有人」就是全队每人一份。");

        UiFactory.Section(Content, "物品生成器");
        UiFactory.SearchField(Content, "搜索物品名称…", OnSearchChanged);
        UiFactory.ChipGroup(Content, CategoryNames, 7, () => categoryIndex, OnCategorySelected);
        itemHint = UiFactory.Hint(Content, "正在载入物品列表…");
        itemList = UiFactory.List(Content, "ItemList", CellSpacing);
        UiFactory.Hint(Content, "右键点击格子可以收藏 / 取消收藏，收藏的物品会出现在「收藏」分类里，并且会保存到本地。");

        UiFactory.Section(Content, "怪物生成器");
        mobHint = UiFactory.Hint(Content, "点一下就在你身前的地面刷一只怪出来（只有房主能生成）。");
        for (var start = 0; start < MobSpawner.Entries.Length; start += 3)
        {
            var row = UiFactory.HorizontalRow(Content);
            var end = Mathf.Min(start + 3, MobSpawner.Entries.Length);

            for (var i = start; i < end; i++)
            {
                var entry = MobSpawner.Entries[i];
                UiFactory.Button(row, entry.Label, () => SpawnMob(entry));
            }
        }

        UiFactory.Hint(Content, "怪不是物品（不在游戏的物品库里），所以上面的物品生成器刷不出来，这里用游戏自己刷怪的方式生成。"
            + "有些怪要开了对应的「危险」设置才会主动攻击；能捡起来的那种怪（蝎子、蜂巢）本身就是物品，在上面搜名字就能刷。"
            + "蜘蛛、蚁狮、秃鹰、青蛙舌、野猪机器人、水豚是随地图预置在关卡里的，游戏里没有能加载的预制体，刷不出来。");
    }

    internal override void OnShown()
    {
        ItemCatalog.EnsureLoaded();
        RefreshTarget();

        var key = $"{categoryIndex}|{ItemFavorites.Version}|{searchText}";
        if (key != builtKey)
        {
            PopulateItems();
        }
    }

    // ---------------------------------------------------------------- 列表渲染

    private void OnSearchChanged(string text)
    {
        searchText = text ?? string.Empty;
        PopulateItems();
    }

    private void OnCategorySelected(int index)
    {
        categoryIndex = index;
        PopulateItems();
    }

    private void PopulateItems()
    {
        if (itemList == null)
        {
            return;
        }

        builtKey = $"{categoryIndex}|{ItemFavorites.Version}|{searchText}";
        Clear(itemList);

        var total = ItemCatalog.Items.Count;
        var matched = new List<Item>();

        foreach (var item in ItemCatalog.Items)
        {
            if (matched.Count >= MaxDisplayed)
            {
                break;
            }

            if (item == null || !MatchesCategory(item) || !MatchesSearch(item))
            {
                continue;
            }

            matched.Add(item);
        }

        // 数据库顺序每次启动都可能变，这里重排一次，分类里的排列才不会跳来跳去。
        matched.Sort(ItemCatalog.CompareForDisplay);

        for (var start = 0; start < matched.Count; start += Columns)
        {
            var row = UiFactory.GridRow(itemList, CellSize, CellSpacing);
            var end = Mathf.Min(start + Columns, matched.Count);

            for (var i = start; i < end; i++)
            {
                var captured = matched[i];
                var label = (ItemFavorites.Contains(captured) ? "★ " : string.Empty) + ItemCatalog.DisplayName(captured);

                UiFactory.IconTile(
                    row,
                    UiFactory.IconSprite(ItemCatalog.Icon(captured)),
                    label,
                    () => Give(captured),
                    () => ToggleFavorite(captured),
                    CellSize,
                    false);
            }
        }

        if (itemHint == null)
        {
            return;
        }

        if (matched.Count == 0)
        {
            itemHint.text = categoryIndex == 0
                ? "还没有收藏任何物品。在物品格子上点右键就能收藏，收藏会保存到本地。"
                : "没有匹配的物品，换个关键字或分类试试。";
        }
        else
        {
            var suffix = matched.Count >= MaxDisplayed ? $"（已达上限 {MaxDisplayed}）" : string.Empty;
            itemHint.text = $"显示 {matched.Count} / {total} 件物品{suffix}";
        }
    }

    private bool MatchesCategory(Item item)
    {
        var category = ItemCatalog.CategoryOf(item);

        switch (categoryIndex)
        {
            case 0:
                return ItemFavorites.Contains(item);
            case 1:
                return true;
            case 2:
                return category == ItemCategory.Food;
            case 3:
                return category == ItemCategory.Tool;
            case 4:
                return category == ItemCategory.Gear;
            case 5:
                return category == ItemCategory.Mystic;
            default:
                return category == ItemCategory.None;
        }
    }

    private bool MatchesSearch(Item item)
    {
        if (searchText.Length == 0)
        {
            return true;
        }

        return ItemCatalog.DisplayName(item).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
            || item.name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void ToggleFavorite(Item item)
    {
        ItemFavorites.Toggle(item);
        PopulateItems();
    }

    // ---------------------------------------------------------------- 发放目标

    private void RefreshTarget()
    {
        var local = Character.localCharacter;
        string display;

        if (targetAll)
        {
            display = "房间内所有人";
        }
        else if (targetCharacter == null || ReferenceEquals(targetCharacter, local))
        {
            targetAll = false;
            targetCharacter = null;
            display = "自己";
        }
        else
        {
            display = SafeCharacterName(targetCharacter);
        }

        if (targetText != null)
        {
            targetText.text = $"当前发放目标：{display}";
        }
    }

    private void CycleTarget()
    {
        var local = Character.localCharacter;
        var teammates = new List<Character>();

        foreach (var character in GetCharacters())
        {
            if (!ReferenceEquals(character, local))
            {
                teammates.Add(character);
            }
        }

        // 选项顺序：自己 → 每位队友 → 房间内所有人
        var options = teammates.Count + 2;
        var current = 0;

        if (targetAll)
        {
            current = options - 1;
        }
        else if (targetCharacter != null)
        {
            var index = teammates.IndexOf(targetCharacter);
            current = index >= 0 ? index + 1 : 0;
        }

        current = (current + 1) % options;

        if (current == 0)
        {
            targetAll = false;
            targetCharacter = null;
        }
        else if (current == options - 1)
        {
            targetAll = true;
            targetCharacter = null;
        }
        else
        {
            targetAll = false;
            targetCharacter = teammates[current - 1];
        }

        RefreshTarget();
    }

    private void Give(Item prefab)
    {
        if (prefab == null)
        {
            return;
        }

        try
        {
            var utils = GameUtils.instance;
            if (utils == null)
            {
                Plugin.Log.LogWarning("游戏还没准备好（GameUtils 为空），稍后再试。");
                return;
            }

            if (targetAll)
            {
                var characters = GetCharacters();
                if (characters.Count == 0)
                {
                    Plugin.Log.LogWarning("房间里没有玩家。");
                    return;
                }

                foreach (var character in characters)
                {
                    utils.InstantiateAndGrab(prefab, character);
                }

                return;
            }

            var target = targetCharacter ?? Character.localCharacter;
            if (target == null)
            {
                Plugin.Log.LogWarning("还没有进入一局游戏，无法发放物品。");
                return;
            }

            utils.InstantiateAndGrab(prefab, target);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"发放物品失败: {e}");
        }
    }

    // ---------------------------------------------------------------- 怪物生成器

    private void SpawnMob(MobSpawner.Entry entry)
    {
        var message = MobSpawner.Spawn(entry);

        if (mobHint != null)
        {
            mobHint.text = message;
        }
    }

    // ---------------------------------------------------------------- 手持物品

    private static Item? HeldItem()
    {
        var character = Character.localCharacter;
        if (character == null || character.data == null)
        {
            return null;
        }

        return character.data.currentItem;
    }

    private static void RepairHeldItem()
    {
        var item = HeldItem();
        if (item == null)
        {
            Plugin.Log.LogWarning("手上没有物品。");
            return;
        }

        try
        {
            item.SetUseRemainingPercentage(1f);

            var uses = item.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
            if (uses.HasData)
            {
                uses.Value = item.totalUses;
            }

            item.WasActive();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"修复手持物品失败: {e}");
        }
    }

    private static void RefillHeldItem()
    {
        var item = HeldItem();
        if (item == null)
        {
            Plugin.Log.LogWarning("手上没有物品。");
            return;
        }

        try
        {
            item.SetUseRemainingPercentage(1f);

            if (item.HasData(DataEntryKey.Fuel))
            {
                item.GetData<FloatItemData>(DataEntryKey.Fuel).Value = 1f;
            }

            item.WasActive();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"充满手持物品能量失败: {e}");
        }
    }

    private static void FinishCookingHeldItem()
    {
        var item = HeldItem();
        if (item == null)
        {
            Plugin.Log.LogWarning("手上没有物品。");
            return;
        }

        try
        {
            var cooking = item.GetComponent<ItemCooking>();
            if (cooking == null || !cooking.canBeCooked)
            {
                Plugin.Log.LogWarning("这件物品不能烹饪。");
                return;
            }

            cooking.FinishCooking();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"完成烹饪失败: {e}");
        }
    }

    private static void DropAllItems()
    {
        var character = Character.localCharacter;
        if (character == null || character.refs == null || character.refs.items == null)
        {
            return;
        }

        try
        {
            character.refs.items.DropAllItems(true);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"丢弃物品失败: {e}");
        }
    }

    // ---------------------------------------------------------------- 工具

    private static string SafeCharacterName(Character character)
    {
        try
        {
            var name = character.characterName;
            return string.IsNullOrEmpty(name) ? "玩家" : name;
        }
        catch (Exception)
        {
            return "玩家";
        }
    }

    private static List<Character> GetCharacters()
    {
        var result = new List<Character>();

        try
        {
            var characters = PlayerHandler.GetAllPlayerCharacters();
            if (characters != null)
            {
                foreach (var character in characters)
                {
                    if (character != null && character.data != null)
                    {
                        result.Add(character);
                    }
                }
            }
        }
        catch (Exception)
        {
            // 未进入游戏时忽略。
        }

        return result;
    }

    private static void Clear(RectTransform host)
    {
        for (var i = host.childCount - 1; i >= 0; i--)
        {
            UnityEngine.Object.Destroy(host.GetChild(i).gameObject);
        }
    }
}
