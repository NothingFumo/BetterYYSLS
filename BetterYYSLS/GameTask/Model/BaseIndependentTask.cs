using BetterYYSLS.Helpers;
using Vanara.PInvoke;

namespace BetterYYSLS.GameTask.Model;

public class BaseIndependentTask
{
    protected SystemInfo Info => TaskContext.Instance().SystemInfo;
    protected RECT CaptureRect => TaskContext.Instance().SystemInfo.CaptureAreaRect;
    protected double AssetScale => TaskContext.Instance().SystemInfo.AssetScale;
}
