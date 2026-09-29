using System.Text;

namespace PeakMasterMenu.UI.Stamina;

/// <summary>
/// 按 UTF-8 字节数截断字符串。
///
/// 移植自 PeakStatsEx（LengSword，MIT 许可）的 <c>StringExtensions.TrimToByteLength</c>。
/// 队友名字里可能带中文（一个汉字 3 字节），按"字符数"截会截出半个字，
/// 所以这里按字节截、再把结尾那个残缺字符（U+FFFD）削掉。
/// </summary>
internal static class TextTruncation
{
    internal static string TrimToByteLength(this string input, int byteLength)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var bytes = Encoding.UTF8.GetBytes(input);

        if (bytes.Length <= byteLength)
        {
            return input;
        }

        var trimmed = Encoding.UTF8.GetString(bytes, 0, byteLength);
        var length = trimmed.Length;

        while (length > 0 && trimmed[length - 1] == '\uFFFD')
        {
            length--;
        }

        return trimmed.Substring(0, length);
    }
}
