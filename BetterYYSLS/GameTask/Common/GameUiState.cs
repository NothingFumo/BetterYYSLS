using BetterYYSLS.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;

namespace BetterYYSLS.GameTask.Common;

/// <summary>
/// 燕云界面状态判断。
/// 剧情对话/过场使用“上下纯黑电影边 + 中间画面非全黑”作为结构化锚点。
/// 不使用按键提示模板，也不把普通菜单里的深色背景当成对话。
/// </summary>
public static class GameUiState
{
    /// <summary>黑边消失后继续保持对话状态的时间，避免过场切换瞬间误判</summary>
    private const int TalkHoldSeconds = 3;

    private static readonly object SyncRoot = new();

    /// <summary>最近一次确认处于对话界面的时间</summary>
    private static DateTime _lastTalkTime = DateTime.MinValue;

    /// <summary>
    /// 判断当前界面类型。当前只区分剧情对话和普通界面。
    /// </summary>
    public static GameUiCategory WhichGameUi(ImageRegion area)
    {
        return IsInTalkUi(area) ? GameUiCategory.Talk : GameUiCategory.Main;
    }

    /// <summary>
    /// 判断是否处于剧情对话或过场界面。
    /// 上下 7%/12% 必须接近纯黑，且中间剧情画面不能是纯黑加载页。
    /// </summary>
    public static bool IsInTalkUi(ImageRegion area)
    {
        try
        {
            var width = area.Width;
            var height = area.Height;
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            lock (SyncRoot)
            {
                if ((DateTime.Now - _lastTalkTime).TotalSeconds <= TalkHoldSeconds)
                {
                    return true;
                }
            }

            var gray = area.SrcGreyMat;
            if (!IsLetterbox(gray, width, height))
            {
                return false;
            }

            lock (SyncRoot)
            {
                _lastTalkTime = DateTime.Now;
            }

            return true;
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogDebug(e, "对话界面判断失败");
            return false;
        }
    }

    /// <summary>
    /// 上下电影黑边检测。纯黑阈值取 15，避免夜晚场景的深色地面被当成黑边。
    /// </summary>
    private static bool IsLetterbox(Mat gray, int width, int height)
    {
        var topHeight = Math.Max(1, (int)(height * 0.07));
        var topDark = DarkRatio(gray, 0, topHeight, 15);

        var bottomHeight = Math.Max(1, (int)(height * 0.12));
        var bottomDark = DarkRatio(gray, height - bottomHeight, bottomHeight, 15);

        if (topDark < 0.85 || bottomDark < 0.85)
        {
            return false;
        }

        using var center = new Mat(gray, new Rect(
            (int)(width * 0.25), (int)(height * 0.3),
            (int)(width * 0.5), (int)(height * 0.35)));
        return Cv2.Mean(center).Val0 > 40;
    }

    /// <summary>
    /// 统计指定水平带内近黑像素的比例。
    /// </summary>
    private static double DarkRatio(Mat gray, int y, int height, int maxValue)
    {
        using var band = new Mat(gray, new Rect(0, y, gray.Width, height));
        using var mask = new Mat();
        Cv2.InRange(band, new Scalar(0), new Scalar(maxValue), mask);
        return Cv2.CountNonZero(mask) / (double)(band.Width * band.Height);
    }
}
