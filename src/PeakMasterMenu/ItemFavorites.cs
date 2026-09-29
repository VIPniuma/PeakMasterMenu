using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using PeakMasterMenu.UI;

namespace PeakMasterMenu;

/// <summary>
/// 收藏的物品名单，物品页和绘制页共用。
/// 名单写在 BepInEx 的配置文件里（赋值即落盘），所以关掉游戏再进来依然还在。
/// </summary>
internal static class ItemFavorites
{
    private const char Separator = ';';

    private static readonly HashSet<string> keys = new(StringComparer.Ordinal);
    private static ConfigEntry<string>? entry;

    /// <summary>每次收藏发生变化就自增，界面靠它判断要不要重建列表。</summary>
    internal static int Version { get; private set; }

    internal static int Count => keys.Count;

    /// <summary>由 <see cref="Plugin"/> 在启动时调用一次。</summary>
    internal static void Initialize(ConfigFile config)
    {
        entry = config.Bind(
            "Items",
            "Favorites",
            string.Empty,
            "收藏的物品（由菜单的右键收藏自动维护，一般不需要手改）。");

        keys.Clear();

        try
        {
            var saved = entry.Value ?? string.Empty;
            foreach (var chunk in saved.Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries))
            {
                var key = chunk.Trim();
                if (key.Length > 0)
                {
                    keys.Add(key);
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"读取收藏列表失败: {e.Message}");
        }

        Plugin.Log.LogInfo($"已载入 {keys.Count} 件收藏物品。");
    }

    internal static bool Contains(Item? item)
    {
        if (item == null)
        {
            return false;
        }

        // 顺手认一下早期的按物体名字存的记录。
        return keys.Contains(ItemCatalog.Key(item)) || keys.Contains(item.name);
    }

    /// <summary>收藏 / 取消收藏一件物品，并立刻写进配置文件。</summary>
    internal static void Toggle(Item? item)
    {
        if (item == null)
        {
            return;
        }

        var key = ItemCatalog.Key(item);
        var removed = keys.Remove(key);
        removed |= keys.Remove(item.name);

        if (!removed)
        {
            keys.Add(key);
        }

        Save();
    }

    private static void Save()
    {
        Version++;

        if (entry == null)
        {
            return;
        }

        try
        {
            // 排序后写出，配置文件读起来稳定、也方便手工比对。
            var sorted = new List<string>(keys);
            sorted.Sort(StringComparer.Ordinal);
            entry.Value = string.Join(Separator.ToString(), sorted);

            // 不依赖 SaveOnConfigSet，直接落盘，保证下次开游戏收藏还在。
            entry.ConfigFile.Save();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"保存收藏列表失败: {e.Message}");
        }
    }
}
