using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace PeakMasterMenu.UI.Tabs;

/// <summary>
/// 绘制页：所有「画在目标头上」的东西都归这里 —— 队友标记、地面物品、怪物 / 敌人、
/// 行李箱 / 复活宝箱、雕像。
///
/// 打开「绘制地面物品」后默认一件都不画，只画勾选过的物品——所以要自己勾。
/// </summary>
internal sealed class DrawTab : MenuTab
{
    private const int Columns = 6;
    private const float CellSize = 162f;
    private const float CellSpacing = 10f;
    private const int MaxDisplayed = 240;

    private RectTransform itemList = null!;
    private TextMeshProUGUI itemHint = null!;

    private bool onlyFavorites;
    private string searchText = string.Empty;
    private string builtKey = string.Empty;

    internal override string Title => "绘制";

    protected override void Build()
    {
        UiFactory.Section(Content, "屏幕绘制");
        UiFactory.Switch(
            Content,
            "标记队友位置",
            Plugin.ShowTeammateMarkers.Value,
            value => Plugin.ShowTeammateMarkers.Value = value);
        UiFactory.Hint(Content, "在每个队友头顶画一个标记，联机时用来确认谁在哪（不开就什么都不画）。");

        UiFactory.Switch(Content, "绘制地面物品", Cheats.DrawItems, OnDrawItemsChanged);
        UiFactory.SliderRow(Content, "绘制距离（米）", 20f, 500f, Cheats.DrawDistance, value => Cheats.DrawDistance = value, "0");
        UiFactory.Switch(Content, "绘制怪物 / 敌人位置", Cheats.DrawMobs, value => Cheats.DrawMobs = value);
        UiFactory.SliderRow(
            Content,
            "敌人绘制距离（米）",
            20f,
            400f,
            Cheats.DrawMobDistance,
            value => Cheats.DrawMobDistance = value,
            "0");
        UiFactory.Switch(Content, "绘制行李箱 / 复活宝箱", Cheats.DrawLuggage, value => Cheats.DrawLuggage = value);
        UiFactory.Switch(Content, "绘制雕像", Cheats.DrawStatues, value => Cheats.DrawStatues = value);
        UiFactory.Hint(
            Content,
            "标记画在目标正上方，大小固定、不随距离变化，带名称和距离，被墙挡住也照样显示。"
            + "行李箱 / 复活宝箱和雕像不算普通道具（它们不在物品目录里），所以用上面这两个开关单独控制，"
            + "距离跟「绘制距离」共用。"
            + "雕像连其它地图分段里的一起画，标签上还会写出石像手里那颗护符的名字（童军的野心等），"
            + "只抱着护符、没有雕像组件的石像也能靠护符找出来。"
            + "「绘制怪物 / 敌人位置」画的是甲虫、蝎子、蜜蜂群、蘑菇僵尸、风滚草、蜘蛛、蚁狮、秃鹰、青蛙舌、"
            + "水豚、野猪机器人、食人花、水母、骷髅、乌鸦、BingBong、童子军大师这些生物；"
            + "只画当前地图分段里激活着的，还没走到的那段不画。");

        // 选择网格常驻显示，不再跟着「绘制地面物品」一起隐藏：
        // 隐藏之后这一页滚到下面就只剩几个开关，看上去就像绘制功能没了；
        // 而且开关一开一关，下面整块内容还会跟着跳。
        UiFactory.Section(Content, "选择要绘制的物品");
        UiFactory.SearchField(Content, "搜索物品名称…", OnSearchChanged);

        var bulk = UiFactory.HorizontalRow(Content, 46f);
        UiFactory.Button(bulk, "勾选当前筛选", SelectVisible, 20f);
        UiFactory.Button(bulk, "清空勾选", ClearSelection, 20f);
        UiFactory.Button(bulk, "勾选全部收藏", SelectFavorites, 20f);

        UiFactory.Switch(Content, "只看收藏的物品", onlyFavorites, OnOnlyFavoritesChanged);
        itemHint = UiFactory.Hint(Content, string.Empty);
        itemList = UiFactory.List(Content, "DrawItemList", CellSpacing);
        UiFactory.Hint(Content, "默认一件都不画：绿色的格子才会被绘制，点一下切换；右键收藏，收藏会存到本地。");
    }

    internal override void OnShown()
    {
        if (BuildKey != builtKey)
        {
            PopulateItems();
        }
    }

    // ---------------------------------------------------------------- 开关联动

    private void OnDrawItemsChanged(bool value)
    {
        Cheats.DrawItems = value;

        // 下面的提示语里会写明开关有没有打开，所以跟着重铺一次。
        PopulateItems();
    }

    private void OnOnlyFavoritesChanged(bool value)
    {
        onlyFavorites = value;
        PopulateItems();
    }

    // ---------------------------------------------------------------- 勾选网格

    private void OnSearchChanged(string text)
    {
        searchText = text ?? string.Empty;
        PopulateItems();
    }

    /// <summary>把当前筛选出来的物品一次性勾上。</summary>
    private void SelectVisible()
    {
        var added = 0;
        foreach (var item in ItemCatalog.Items)
        {
            if (added >= MaxDisplayed)
            {
                break;
            }

            if (item == null || !MatchesSearch(item) || !MatchesFavoriteFilter(item))
            {
                continue;
            }

            if (Cheats.DrawItemFilter.Add(ItemCatalog.Key(item)))
            {
                added++;
            }
        }

        PopulateItems();
    }

    private void ClearSelection()
    {
        Cheats.DrawItemFilter.Clear();
        PopulateItems();
    }

    /// <summary>收藏列表在这里的用处：一键把收藏过的物品都勾上绘制。</summary>
    private void SelectFavorites()
    {
        foreach (var item in ItemCatalog.Items)
        {
            if (item != null && ItemFavorites.Contains(item) && MatchesSearch(item))
            {
                Cheats.DrawItemFilter.Add(ItemCatalog.Key(item));
            }
        }

        PopulateItems();
    }

    private void ToggleFavorite(Item item)
    {
        ItemFavorites.Toggle(item);
        PopulateItems();
    }

    private string BuildKey =>
        $"{Cheats.DrawItems}|{Cheats.DrawItemFilter.Count}|{ItemFavorites.Version}|{onlyFavorites}|{searchText}";

    private void PopulateItems()
    {
        if (itemList == null)
        {
            return;
        }

        builtKey = BuildKey;
        Clear(itemList);

        var matched = new List<Item>();
        foreach (var item in ItemCatalog.Items)
        {
            if (matched.Count >= MaxDisplayed)
            {
                break;
            }

            if (item == null || !MatchesSearch(item) || !MatchesFavoriteFilter(item))
            {
                continue;
            }

            matched.Add(item);
        }

        // 数据库顺序每次启动都可能变，这里重排一次，格子的排列才不会跳来跳去。
        matched.Sort(ItemCatalog.CompareForDisplay);

        for (var start = 0; start < matched.Count; start += Columns)
        {
            var row = UiFactory.GridRow(itemList, CellSize, CellSpacing);
            var end = Mathf.Min(start + Columns, matched.Count);

            for (var i = start; i < end; i++)
            {
                var captured = matched[i];
                UiFactory.IconTile(
                    row,
                    UiFactory.IconSprite(ItemCatalog.Icon(captured)),
                    TileLabel(captured),
                    () => Toggle(captured),
                    () => ToggleFavorite(captured),
                    CellSize,
                    ShouldDraw(captured),
                    UiFactory.OnBg);
            }
        }

        if (itemHint == null)
        {
            return;
        }

        itemHint.text = !Cheats.DrawItems
            ? $"「绘制地面物品」还没打开，先打开上面的开关；本页 {matched.Count} 件，点一下格子即可勾选。"
            : Cheats.DrawItemFilter.Count == 0
                ? $"还没有勾选任何物品，屏幕上一件都不会画（本页 {matched.Count} 件，点一下格子即可勾选）。"
                : onlyFavorites
                    ? $"只看收藏：本页 {matched.Count} 件，已勾选 {Cheats.DrawItemFilter.Count} 件。"
                    : $"已勾选 {Cheats.DrawItemFilter.Count} 件要绘制的物品（本页 {matched.Count} 件）。";
    }

    private static string TileLabel(Item item)
    {
        var name = ItemCatalog.DisplayName(item);
        return ItemFavorites.Contains(item) ? "★ " + name : name;
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

    private bool MatchesFavoriteFilter(Item item)
    {
        return !onlyFavorites || ItemFavorites.Contains(item);
    }

    /// <summary>只有勾选过的物品才会被绘制，清单为空就什么都不画。</summary>
    private static bool ShouldDraw(Item item)
    {
        return Cheats.DrawItemFilter.Contains(ItemCatalog.Key(item));
    }

    private void Toggle(Item item)
    {
        var key = ItemCatalog.Key(item);
        if (!Cheats.DrawItemFilter.Remove(key))
        {
            Cheats.DrawItemFilter.Add(key);
        }

        PopulateItems();
    }

    private static void Clear(RectTransform host)
    {
        for (var i = host.childCount - 1; i >= 0; i--)
        {
            UnityEngine.Object.Destroy(host.GetChild(i).gameObject);
        }
    }
}
