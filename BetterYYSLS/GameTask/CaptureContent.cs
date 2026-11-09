using BetterYYSLS.GameTask.Common;
using BetterYYSLS.GameTask.Model.Area;
using System;
using System.Drawing;

namespace BetterYYSLS.GameTask;

/// <summary>
/// 捕获的内容
/// 以及一些多个trigger会用到的内容
/// </summary>
public class CaptureContent : IDisposable
{
    public static readonly int MaxFrameIndexSecond = 60;
    public Bitmap SrcBitmap { get; }
    public int FrameIndex { get; }
    public double TimerInterval { get; }

    public int FrameRate => (int)(1000 / TimerInterval);

    public ImageRegion CaptureRectArea { get; private set; }

    /// <summary>当前帧的界面类型，由调度器填充</summary>
    public GameUiCategory CurrentGameUiCategory { get; set; } = GameUiCategory.Unknown;

    public CaptureContent(Bitmap srcBitmap, int frameIndex, double interval)
    {
        SrcBitmap = srcBitmap;
        FrameIndex = frameIndex;
        TimerInterval = interval;
        var systemInfo = TaskContext.Instance().SystemInfo;

        var gameCaptureRegion = systemInfo.DesktopRectArea.Derive(srcBitmap, systemInfo.CaptureAreaRect.X, systemInfo.CaptureAreaRect.Y);
        CaptureRectArea = gameCaptureRegion.DeriveTo1080P();
    }

    public void Dispose()
    {
        CaptureRectArea.Dispose();
        SrcBitmap.Dispose();
    }
}
