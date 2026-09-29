using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace PeakMasterMenu;

/// <summary>
/// 物品的「类型 + 描述」数据。
///
/// 注意：游戏本身<b>没有</b>物品描述文本，原版 Peak Item Tooltip 是作者手写了一份 JSON
/// （<c>peak-item-tooltip.descriptions[.zh-CN].json</c>）。这里照搬同一套做法：
/// 先读 BepInEx/config 下那个文件 —— 和原版<b>共用同一份</b>，你改哪边两边都会变；
/// 文件不存在就把本 Mod 内置的默认数据写出去，再用它。
/// </summary>
internal sealed class ItemDescriptions
{
    /// <summary>单个物品的类型与描述。</summary>
    internal sealed class Entry
    {
        internal List<string> Types { get; } = new List<string>();

        internal string Description { get; set; } = string.Empty;
    }

    private const string FileStem = "peak-item-tooltip.descriptions";

    private static readonly string[] Sections = { "types", "items" };

    private readonly Dictionary<string, Entry> items = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Color> typeColours = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

    private DateTime lastWriteUtc;

    internal ItemDescriptions()
    {
        FilePath = DefaultFilePath;
        Reload();
    }

    /// <summary>和原版同名同路径，装了原版 mod 的话两边共用这一份文件。</summary>
    private static string DefaultFilePath => Path.Combine(
        Paths.ConfigPath,
        FileStem + (ItemTooltipConfig.Chinese ? ".zh-CN" : string.Empty) + ".json");

    /// <summary>描述文件的实际路径（配置目录下，刻意与原版同名）。</summary>
    internal string FilePath { get; }

    /// <summary>查一个物品的类型 / 描述，键就是 <c>Item.UIData.itemName</c>。</summary>
    internal bool TryGet(string? itemName, out Entry entry)
    {
        entry = null!;
        if (string.IsNullOrEmpty(itemName))
        {
            return false;
        }

        return items.TryGetValue(itemName!, out entry!);
    }

    /// <summary>查一个类型对应的颜色。</summary>
    internal bool TryGetTypeColour(string type, out Color colour) => typeColours.TryGetValue(type, out colour);

    /// <summary>
    /// 文件被改动过就重新读一遍，返回是否真的重载了。
    /// </summary>
    internal bool MaybeReload()
    {
        if (!ItemTooltipConfig.HotReload.Value)
        {
            return false;
        }

        try
        {
            if (!File.Exists(FilePath))
            {
                return false;
            }

            var writeTime = File.GetLastWriteTimeUtc(FilePath);
            if (writeTime == lastWriteUtc)
            {
                return false;
            }

            lastWriteUtc = writeTime;
            Reload();
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"重载物品描述失败: {e.Message}");
            return false;
        }
    }

    private void Reload()
    {
        var text = ReadBuiltIn();
        if (text == null)
        {
            items.Clear();
            typeColours.Clear();
            Plugin.Log.LogWarning("内置的物品描述数据缺失，提示框只会显示名称和数值。");
            return;
        }

        try
        {
            if (File.Exists(FilePath))
            {
                text = Merge(text, File.ReadAllText(FilePath));
                lastWriteUtc = File.GetLastWriteTimeUtc(FilePath);
            }
            else
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(FilePath, text);
                lastWriteUtc = File.GetLastWriteTimeUtc(FilePath);
                Plugin.Log.LogInfo($"已生成物品描述文件：{FilePath}（可以直接编辑，改完会自动重载）");
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"读写物品描述文件失败，改用内置数据: {e.Message}");
        }

        Parse(text);

        if (items.Count == 0)
        {
            Plugin.Log.LogWarning($"物品描述里一条记录都没解析出来：{FilePath}");
        }
    }

    /// <summary>取内置的默认描述数据（随 dll 一起打包）。</summary>
    private static string? ReadBuiltIn()
    {
        var name = "PeakMasterMenu.item-descriptions"
            + (ItemTooltipConfig.Chinese ? ".zh-CN" : string.Empty)
            + ".json";

        using var stream = typeof(ItemDescriptions).Assembly.GetManifestResourceStream(name);
        if (stream == null)
        {
            return null;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// 用户文件覆盖内置数据：用户文件里写了的以用户为准，没写的用内置的补上。
    /// </summary>
    private static string Merge(string builtInText, string userText)
    {
        var builtIn = JObject.Parse(builtInText);
        var user = JObject.Parse(userText);

        foreach (var section in Sections)
        {
            if (builtIn[section] is not JObject fallback)
            {
                continue;
            }

            if (user[section] is not JObject mine)
            {
                user[section] = fallback.DeepClone();
                continue;
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in mine.Properties())
            {
                names.Add(property.Name);
            }

            foreach (var property in fallback.Properties())
            {
                if (names.Add(property.Name))
                {
                    mine[property.Name] = property.Value.DeepClone();
                }
            }
        }

        return user.ToString();
    }

    private void Parse(string json)
    {
        items.Clear();
        typeColours.Clear();

        var root = JObject.Parse(json);

        if (root["types"] is JObject types)
        {
            foreach (var property in types.Properties())
            {
                var html = property.Value.ToString();
                if (!html.StartsWith("#", StringComparison.Ordinal))
                {
                    html = "#" + html;
                }

                if (ColorUtility.TryParseHtmlString(html, out var colour))
                {
                    typeColours[property.Name] = colour;
                }
            }
        }

        if (root["items"] is not JObject list)
        {
            return;
        }

        foreach (var property in list.Properties())
        {
            var entry = new Entry();

            if (property.Value is JObject record)
            {
                entry.Description = record["description"]?.ToString() ?? string.Empty;

                switch (record["type"])
                {
                    // "type": ["MYSTICAL", "DEPLOYABLE"]
                    case JArray array:
                        foreach (var value in array)
                        {
                            AddType(entry, value.ToString());
                        }

                        break;

                    // "type": "CONSUMABLE"，也容忍 "A / B" 这种写法
                    case not null:
                        foreach (var part in record["type"]!.ToString()
                                     .Split(new[] { '/', ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            AddType(entry, part);
                        }

                        break;
                }
            }

            items[property.Name] = entry;
        }
    }

    private static void AddType(Entry entry, string type)
    {
        type = type.Trim();
        if (type.Length > 0)
        {
            entry.Types.Add(type);
        }
    }
}
