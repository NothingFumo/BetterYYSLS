using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BetterYYSLS.GameTask.AutoSkip;

/// <summary>
/// 剧情底部按键提示的解析结果。数据来源为底部提示条的 OCR 文本，
/// 例如 “Esc 跳过  R 3.0 倍速  F1 评论  T 暂停  V 剧情回顾”。
/// </summary>
public readonly record struct AutoSkipHintSnapshot
{
    /// <summary>是否出现 “Esc 跳过” 提示，出现时可按 Esc 整段跳过</summary>
    public bool CanSkip { get; }

    /// <summary>
    /// 是否处于剧情播放中。底部提示只在播放时显示 “T 暂停”，
    /// 暂停后同一位置会变成 “T 继续”，因此只认 “暂停”。
    /// </summary>
    public bool IsPlaying { get; }

    /// <summary>当前播放倍速；未识别到 “倍速” 提示时为 0</summary>
    public double Speed { get; }

    /// <summary>“Esc 跳过”提示识别，要求 Esc 与跳过相邻，避免把其他提示误判为可跳过</summary>
    private static readonly Regex EscSkipRegex = new(@"esc\s*跳过", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>倍速提示识别，例如 “3.0 倍速” 或 “R3.0倍速”</summary>
    private static readonly Regex SpeedRegex = new(@"([0-9]+(?:\.[0-9]+)?)\s*倍速", RegexOptions.Compiled);

    /// <summary>
    /// 创建提示快照。
    /// </summary>
    public AutoSkipHintSnapshot(bool canSkip, bool isPlaying, double speed)
    {
        CanSkip = canSkip;
        IsPlaying = isPlaying;
        Speed = speed;
    }

    /// <summary>
    /// 解析底部提示条文本。同时要求 “Esc” 与 “跳过” 才认为可整段跳过，避免误按 Esc 打开菜单。
    /// </summary>
    /// <param name="text">底部提示条的 OCR 文本</param>
    /// <returns>解析后的提示快照；空文本返回默认值</returns>
    public static AutoSkipHintSnapshot Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return default;
        }

        var isPlaying = text.Contains("暂停", StringComparison.Ordinal);
        var canSkip = EscSkipRegex.IsMatch(text);

        var speed = 0d;
        var match = SpeedRegex.Match(text);
        if (match.Success &&
            double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            value is > 0 and <= 5)
        {
            speed = value;
        }

        return new AutoSkipHintSnapshot(canSkip, isPlaying, speed);
    }
}
