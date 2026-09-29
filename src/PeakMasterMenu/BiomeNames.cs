using System;
using Zorro.Core;

namespace PeakMasterMenu;

/// <summary>
/// 群系 / 区域名。
///
/// 这游戏每天的地图都不一样：关卡段和每段的群系都是随机重排的
/// （雪山、雾沼、火星、威斯康星……），所以名字只能现读，写死一张对照表迟早是错的
/// —— 之前右上角那个「巨柱」就是写死的产物，游戏里根本没有这个名字。
///
/// 现在只认游戏自己的数据：关卡里的进度点 <c>MountainProgressHandler.ProgressPoint</c>
/// 本身就带着「这个群系叫什么」——<c>biome</c> 是哪个群系，<c>localizedTitle</c>
/// 是游戏自己显示在屏幕上的名字。
/// </summary>
internal static class BiomeNames
{
    /// <summary>
    /// 群系在游戏里显示的名字。
    /// 读不到就退回游戏自己的枚举名（英文），不自己编中文。
    /// </summary>
    internal static string Of(Biome.BiomeType biome)
    {
        var name = FromProgressPoints(biome);
        return string.IsNullOrEmpty(name) ? biome.ToString() : name;
    }

    /// <summary>问关卡里的进度点：这个群系叫什么。</summary>
    private static string? FromProgressPoints(Biome.BiomeType biome)
    {
        try
        {
            var handler = Singleton<MountainProgressHandler>.Instance;
            var points = handler != null ? handler.progressPoints : null;
            if (points == null)
            {
                return null;
            }

            foreach (var point in points)
            {
                if (point == null || point.biome != biome)
                {
                    continue;
                }

                // 游戏自己就是这么取名字显示在屏幕上的。
                var name = point.localizedTitle;
                if (string.IsNullOrEmpty(name))
                {
                    name = LocalizedText.GetText(point.title, LocalizedText.Language.SimplifiedChinese);
                }

                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }
        }
        catch (Exception)
        {
            // 关卡还没准备好 / 语言表没加载好时忽略。
        }

        return null;
    }
}
