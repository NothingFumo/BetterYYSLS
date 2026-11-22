using BetterYYSLS.GameTask.AutoSkip;
using Xunit;

namespace BetterYYSLS.UnitTest;

/// <summary>
/// 剧情底部提示条解析测试。
/// </summary>
public sealed class AutoSkipHintSnapshotTests
{
    [Fact]
    public void Parse_ReadsSkipSpeedAndPlayingHint()
    {
        var hint = AutoSkipHintSnapshot.Parse("Esc 跳过  R 3.0 倍速  F1 评论  T 暂停  V 剧情回顾");

        Assert.True(hint.CanSkip);
        Assert.True(hint.IsPlaying);
        Assert.Equal(3.0, hint.Speed);
    }

    [Fact]
    public void Parse_WithoutSkipHintKeepsSkipDisabled()
    {
        var hint = AutoSkipHintSnapshot.Parse("R 1.25 倍速  F1 评论  T 暂停  V 剧情回顾");

        Assert.False(hint.CanSkip);
        Assert.True(hint.IsPlaying);
        Assert.Equal(1.25, hint.Speed);
    }

    [Fact]
    public void Parse_TreatsResumeHintAsPaused()
    {
        var hint = AutoSkipHintSnapshot.Parse("R 3.0 倍速  T 继续  V 剧情回顾");

        Assert.False(hint.IsPlaying);
        Assert.Equal(3.0, hint.Speed);
    }

    [Fact]
    public void Parse_RequiresEscAdjacentToSkip()
    {
        var hint = AutoSkipHintSnapshot.Parse("Esc 返回  跳过");

        Assert.False(hint.CanSkip);
    }

    [Fact]
    public void Parse_HandlesNullAndMissingSpeed()
    {
        Assert.Equal(default, AutoSkipHintSnapshot.Parse(null));
        Assert.Equal(0d, AutoSkipHintSnapshot.Parse("F1 评论").Speed);
    }
}
