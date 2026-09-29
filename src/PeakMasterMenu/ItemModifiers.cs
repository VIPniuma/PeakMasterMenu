using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Peak;
using UnityEngine;

namespace PeakMasterMenu;

/// <summary>
/// 物品的数值 / 效果那一行 —— 照搬自 Peak Item Tooltip 的 <c>ItemModifiers</c>。
///
/// 每个数值前面带正负号和状态颜色，用的是 TMP 富文本
/// （<c>&lt;color=#RRGGBB&gt;+30 饥饿&lt;/color&gt;</c>），
/// 所以调用方必须把文本的 richText 打开。
/// </summary>
internal static class ItemModifiers
{
    /// <summary>状态类型 → 颜色（原版 ItemModifiers.Colours，按键值一一对应）。</summary>
    private static readonly Dictionary<CharacterAfflictions.STATUSTYPE, Color> Colours =
        new Dictionary<CharacterAfflictions.STATUSTYPE, Color>
        {
            [CharacterAfflictions.STATUSTYPE.Injury] = Hex("#E4463B"),
            [CharacterAfflictions.STATUSTYPE.Hunger] = Hex("#F2C43D"),
            [CharacterAfflictions.STATUSTYPE.Cold] = Hex("#4FA3FF"),
            [CharacterAfflictions.STATUSTYPE.Poison] = Hex("#A65AD8"),
            [CharacterAfflictions.STATUSTYPE.Crab] = Hex("#FF7043"),
            [CharacterAfflictions.STATUSTYPE.Curse] = Hex("#B5179E"),
            [CharacterAfflictions.STATUSTYPE.Drowsy] = Hex("#5AD1C4"),
            [CharacterAfflictions.STATUSTYPE.Weight] = Hex("#B0A08A"),
            [CharacterAfflictions.STATUSTYPE.Hot] = Hex("#FF8C1A"),
            [CharacterAfflictions.STATUSTYPE.Thorns] = Hex("#6FBF4B"),
            [CharacterAfflictions.STATUSTYPE.Spores] = Hex("#9CCC65"),
            [CharacterAfflictions.STATUSTYPE.Web] = Hex("#D0D0D0"),
            [CharacterAfflictions.STATUSTYPE.Arrow] = Hex("#C9A66B"),
            [CharacterAfflictions.STATUSTYPE.Petrify] = Hex("#9E9E9E"),
            [CharacterAfflictions.STATUSTYPE.FlyTrap] = Hex("#4CAF50"),
        };

    private static readonly Color StaminaColour = Hex("#7CD64B");

    private static readonly Color Fallback = Color.white;

    private static readonly StringBuilder Builder = new StringBuilder();

    private static readonly BindingFlags FieldFlags = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>把物品身上所有组件扫一遍，拼出「+30 饥饿」这样的行。</summary>
    internal static string Format(Item? item)
    {
        if (item == null)
        {
            return string.Empty;
        }

        var sb = Builder;
        sb.Clear();
        var first = true;

        foreach (var hunger in item.GetComponentsInChildren<Action_RestoreHunger>(true))
        {
            AppendLine(sb, ref first, ColourFor(CharacterAfflictions.STATUSTYPE.Hunger), -hunger.restorationAmount, Pick("饥饿", "Hunger"));
        }

        foreach (var poison in item.GetComponentsInChildren<Action_InflictPoison>(true))
        {
            AppendLine(sb, ref first, ColourFor(CharacterAfflictions.STATUSTYPE.Poison), poison.poisonPerSecond * poison.inflictionTime, Pick("中毒", "Poison"));
        }

        foreach (var status in item.GetComponentsInChildren<Action_ModifyStatus>(true))
        {
            AppendLine(sb, ref first, ColourFor(status.statusType), status.changeAmount, StatusName(status.statusType));
        }

        foreach (var stamina in item.GetComponentsInChildren<Action_GiveExtraStamina>(true))
        {
            AppendLine(sb, ref first, StaminaColour, stamina.amount, Pick("额外耐力", "Extra Stamina"));
        }

        AppendPetrify(sb, ref first, item);

        return sb.ToString();
    }

    /// <summary>
    /// 石化值没有统一的组件：凡是名字以 <c>petrify</c> 开头的 int / float 字段都算一条，
    /// 再补上治疗宝石自己的区间。
    /// </summary>
    private static void AppendPetrify(StringBuilder sb, ref bool first, Item item)
    {
        var colour = ColourFor(CharacterAfflictions.STATUSTYPE.Petrify);

        foreach (var component in item.GetComponentsInChildren<Component>(true))
        {
            if (component == null)
            {
                continue;
            }

            foreach (var field in component.GetType().GetFields(FieldFlags))
            {
                if (!field.Name.StartsWith("petrify", StringComparison.Ordinal))
                {
                    continue;
                }

                int points;
                if (field.FieldType == typeof(int))
                {
                    points = (int)field.GetValue(component);
                }
                else if (field.FieldType == typeof(float))
                {
                    points = Mathf.RoundToInt((float)field.GetValue(component) * 100f);
                }
                else
                {
                    continue;
                }

                AppendPoints(sb, ref first, colour, points, PetrifyLabel(field.Name));
            }
        }

        foreach (var gem in item.GetComponentsInChildren<Action_HealingGem>(true))
        {
            if (gem == null)
            {
                continue;
            }

            AppendPointsRange(
                sb,
                ref first,
                colour,
                Mathf.RoundToInt(gem.minPetrify * 100f),
                Mathf.RoundToInt(gem.maxPetrify * 100f),
                Pick("石化", "Petrify"));
        }
    }

    /// <summary>小数按「乘以 100 取整」显示成整数点。</summary>
    private static void AppendLine(StringBuilder sb, ref bool first, Color colour, float fraction, string label)
    {
        AppendPoints(sb, ref first, colour, Mathf.RoundToInt(fraction * 100f), label);
    }

    private static void AppendPoints(StringBuilder sb, ref bool first, Color colour, int points, string label)
    {
        if (points == 0)
        {
            return;
        }

        var sign = points > 0 ? "+" : "-";
        var hex = ColorUtility.ToHtmlStringRGB(colour);

        if (!first)
        {
            sb.Append('\n');
        }

        first = false;

        sb.Append("<color=#").Append(hex).Append('>')
          .Append(sign).Append(Mathf.Abs(points)).Append(' ')
          .Append(label)
          .Append("</color>");
    }

    private static void AppendPointsRange(StringBuilder sb, ref bool first, Color colour, int lowPoints, int highPoints, string label)
    {
        if (lowPoints == highPoints)
        {
            AppendPoints(sb, ref first, colour, highPoints, label);
            return;
        }

        if (highPoints == 0)
        {
            return;
        }

        var hex = ColorUtility.ToHtmlStringRGB(colour);

        if (!first)
        {
            sb.Append('\n');
        }

        first = false;

        sb.Append("<color=#").Append(hex).Append('>')
          .Append('+').Append(lowPoints).Append('–').Append(highPoints).Append(' ')
          .Append(label)
          .Append("</color>");
    }

    /// <summary>
    /// <c>petrifyPerSecond</c> → <c>石化（每秒）</c>。
    /// </summary>
    private static string PetrifyLabel(string fieldName)
    {
        const string prefix = "petrify";

        var qualifier = fieldName.Length > prefix.Length
            ? fieldName.Substring(prefix.Length)
            : string.Empty;

        if (string.IsNullOrEmpty(qualifier))
        {
            return Pick("石化", "Petrify");
        }

        if (!ItemTooltipConfig.Chinese)
        {
            return "Petrify (" + qualifier + ")";
        }

        // 把驼峰拆成「空格分隔的小写」再翻，找不到对应的就原样回退。
        var words = new StringBuilder();
        foreach (var c in qualifier)
        {
            if (char.IsUpper(c) && words.Length > 0)
            {
                words.Append(' ');
            }

            words.Append(char.ToLowerInvariant(c));
        }

        return "石化（" + QualifierZh(qualifier) + "）";
    }

    /// <summary>石化字段后缀 → 中文限定词。</summary>
    private static string QualifierZh(string qualifier) => qualifier switch
    {
        "per jump" => "每次跳跃",
        "per use" => "每次使用",
        "per throw" => "每次投掷",
        "per second" => "每秒",
        "per bounce" => "每次弹跳",
        "on hit" => "命中时",
        "on land" => "落地时",
        "on heal" => "治疗时",
        "on burn" => "燃烧时",
        "mystical" => "神秘",
        _ => qualifier,
    };

    private static string StatusName(CharacterAfflictions.STATUSTYPE type)
    {
        if (!ItemTooltipConfig.Chinese)
        {
            return type.ToString();
        }

        return type switch
        {
            CharacterAfflictions.STATUSTYPE.Injury => "受伤",
            CharacterAfflictions.STATUSTYPE.Hunger => "饥饿",
            CharacterAfflictions.STATUSTYPE.Cold => "寒冷",
            CharacterAfflictions.STATUSTYPE.Poison => "中毒",
            CharacterAfflictions.STATUSTYPE.Crab => "螃蟹",
            CharacterAfflictions.STATUSTYPE.Curse => "诅咒",
            CharacterAfflictions.STATUSTYPE.Drowsy => "困倦",
            CharacterAfflictions.STATUSTYPE.Weight => "负重",
            CharacterAfflictions.STATUSTYPE.Hot => "炎热",
            CharacterAfflictions.STATUSTYPE.Thorns => "荆棘",
            CharacterAfflictions.STATUSTYPE.Spores => "孢子",
            CharacterAfflictions.STATUSTYPE.Web => "蛛网",
            CharacterAfflictions.STATUSTYPE.Arrow => "中箭",
            CharacterAfflictions.STATUSTYPE.Petrify => "石化",
            CharacterAfflictions.STATUSTYPE.FlyTrap => "捕蝇草",
            _ => type.ToString(),
        };
    }

    private static Color ColourFor(CharacterAfflictions.STATUSTYPE type) =>
        Colours.TryGetValue(type, out var colour) ? colour : Fallback;

    private static Color Hex(string html) =>
        ColorUtility.TryParseHtmlString(html, out var colour) ? colour : Color.white;

    private static string Pick(string chinese, string english) =>
        ItemTooltipConfig.Chinese ? chinese : english;
}
