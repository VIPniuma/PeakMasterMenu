using System;
using System.Collections.Generic;
using UnityEngine;
using Zorro.Core;

namespace PeakMasterMenu.UI;

/// <summary>物品分类。互斥，每件物品只会落到其中一类。</summary>
internal enum ItemCategory
{
    /// <summary>其它：没有特殊组件的纯道具，比如棋子、石头、向导手册。</summary>
    None,
    Food,
    Tool,
    Gear,
    Mystic,
}

/// <summary>
/// 游戏物品目录的共享缓存。
/// 物品生成器和绘制页都从这里取数据，保证两边的排序、名称、图标完全一致。
/// </summary>
internal static class ItemCatalog
{
    private static readonly List<Item> items = new();
    private static readonly Dictionary<Item, ItemCategory> categories = new();
    private static readonly Dictionary<Item, string> names = new();
    private static readonly Dictionary<ushort, Item> byId = new();
    private static bool loaded;

    internal static IReadOnlyList<Item> Items
    {
        get
        {
            EnsureLoaded();
            return items;
        }
    }

    internal static void EnsureLoaded()
    {
        if (loaded)
        {
            return;
        }

        loaded = true;

        if (!TryLoadFromDatabase())
        {
            try
            {
                foreach (var item in Resources.FindObjectsOfTypeAll<Item>())
                {
                    Add(item);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"枚举物品失败: {e.Message}");
            }
        }

        Plugin.Log.LogInfo($"物品目录载入 {items.Count} 个物品。");
    }

    internal static ItemCategory CategoryOf(Item item)
    {
        EnsureLoaded();
        return categories.TryGetValue(item, out var category) ? category : ItemCategory.None;
    }

    /// <summary>
    /// 物品的稳定标识（用于收藏、绘制筛选，会被写进配置）。
    ///
    /// 场景里的物品是预制体的克隆，名字可能被改过（例如带了 "(Clone)"），
    /// 所以先按 <c>itemID</c> 换算回目录里的预制体，统一用预制体名做键；
    /// 认不出来时才退回用物体自己的名字。
    /// </summary>
    internal static string Key(Item item)
    {
        EnsureLoaded();

        if (item.itemID != 0 && byId.TryGetValue(item.itemID, out var prefab) && prefab != null)
        {
            return prefab.name;
        }

        return item.name;
    }

    internal static string DisplayName(Item item)
    {
        EnsureLoaded();

        if (names.TryGetValue(item, out var cached))
        {
            return cached;
        }

        var name = ResolveName(item);
        names[item] = name;
        return name;
    }

    /// <summary>
    /// 列表展示用的稳定排序。
    ///
    /// <see cref="Items"/> 的先后来自游戏数据库的遍历顺序，其中 itemLookup 是字典，
    /// 每次启动遍历出来的顺序都可能不一样；直接铺进网格，同一个分类里的物品就是乱序的。
    /// 这里统一按预制体名（英文，跨平台稳定）重排，保证各个列表的顺序固定、可预期。
    /// </summary>
    internal static int CompareForDisplay(Item a, Item b)
    {
        var result = string.CompareOrdinal(Key(a), Key(b));
        return result != 0 ? result : string.CompareOrdinal(DisplayName(a), DisplayName(b));
    }

    internal static Texture2D? Icon(Item item)
    {
        try
        {
            if (item.UIData == null)
            {
                return null;
            }

            return item.UIData.GetIcon();
        }
        catch (Exception)
        {
            try
            {
                return item.UIData != null ? item.UIData.icon : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    // ---------------------------------------------------------------- 载入

    private static bool TryLoadFromDatabase()
    {
        try
        {
            var database = SingletonAsset<ItemDatabase>.Instance;
            if (database == null || database.Objects == null)
            {
                return false;
            }

            foreach (var item in database.Objects)
            {
                Add(item);
            }

            // itemLookup 才是「itemID → 预制体」的权威来源，两面都登记一遍，
            // 保证场景里克隆出来的物品也能按 itemID 找回目录里的那一件。
            try
            {
                foreach (var pair in database.itemLookup)
                {
                    if (pair.Value != null && pair.Key != 0)
                    {
                        byId[pair.Key] = pair.Value;
                        Add(pair.Value);
                    }
                }
            }
            catch (Exception)
            {
                // 旧版本没有这个字段也不影响，退回按名字匹配。
            }

            return items.Count > 0;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"读取物品数据库失败: {e.Message}");
            return false;
        }
    }

    private static void Add(Item? item)
    {
        if (item == null || item.UIData == null)
        {
            return;
        }

        if (item.itemID != 0 && !byId.ContainsKey(item.itemID))
        {
            byId[item.itemID] = item;
        }

        if (items.Contains(item))
        {
            return;
        }

        items.Add(item);
        categories[item] = Classify(item);
    }

    /// <summary>
    /// 按物品自身携带的组件与标签推断分类，不写死物品名单，
    /// 所以游戏以后加了新物品也会自动落到合适的分类里。
    /// </summary>
    private static ItemCategory Classify(Item item)
    {
        try
        {
            var tags = item.itemTags;

            // 1) 神秘：护符、仪式匕首这类
            if (Has(tags, Item.ItemTags.Mystical)
                || HasComponent(item, "AmuletBase")
                || HasComponent(item, "RitualDaggerFeedBehavior"))
            {
                return ItemCategory.Mystic;
            }

            // 2) 食品：可烹饪的、蘑菇莓果、曼德拉草
            if (Has(tags, Item.ItemTags.Berry)
                || Has(tags, Item.ItemTags.Mushroom)
                || Has(tags, Item.ItemTags.PackagedFood)
                || Has(tags, Item.ItemTags.GourmandRequirement)
                || item.GetComponent<ItemCooking>() != null
                || HasComponent(item, "Mandrake"))
            {
                return ItemCategory.Food;
            }

            // 3) 装备：各种背包与穿戴物
            if (item is Backpack
                || HasComponent(item, "JetpackItem")
                || HasComponent(item, "Rocketpack")
                || HasComponent(item, "BingBongShieldWhileHolding"))
            {
                return ItemCategory.Gear;
            }

            // 4) 工具：带 ItemComponent 的可用道具（手电、绳索、降落伞……）
            if (item.GetComponent<ItemComponent>() != null)
            {
                return ItemCategory.Tool;
            }
        }
        catch (Exception)
        {
            // 分类失败不影响生成功能，落到「其它」。
        }

        // 5) 其它：棋子、石头、向导手册这类没有特殊组件的物品
        return ItemCategory.None;
    }

    private static bool Has(Item.ItemTags tags, Item.ItemTags flag)
    {
        return (tags & flag) != 0;
    }

    /// <summary>
    /// 按类型名找组件。用字符串是为了不把某个版本特有的类型写进编译期依赖里；
    /// 分类只在载入时算一次，这点开销可以忽略。
    /// </summary>
    private static bool HasComponent(Item item, string typeName)
    {
        foreach (var component in item.GetComponentsInChildren<Component>(true))
        {
            if (component != null && component.GetType().Name == typeName)
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveName(Item item)
    {
        try
        {
            var name = item.GetName();
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }
        }
        catch (Exception)
        {
            // 本地化还没准备好时忽略。
        }

        try
        {
            if (item.UIData != null && !string.IsNullOrEmpty(item.UIData.itemName))
            {
                return item.UIData.itemName;
            }
        }
        catch (Exception)
        {
            // 忽略。
        }

        return item.name;
    }
}
