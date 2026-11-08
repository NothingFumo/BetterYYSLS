using BetterYYSLS.Core.Config;
using BetterYYSLS.Service;
using Microsoft.Extensions.Logging;
using System;

namespace BetterYYSLS.GameTask.Sample;

/// <summary>
/// 示例触发器：演示获取画面、读取配置和输出日志。
/// 实际接入游戏时，可复制本文件并替换 <see cref="OnCapture"/> 中的识别与操作逻辑。
/// </summary>
public class SampleTrigger : ITaskTrigger
{
    /// <summary>
    /// 触发器名称
    /// </summary>
    public string Name => "示例触发器";

    /// <summary>
    /// 是否启用，由示例触发器配置控制
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// 执行优先级，越大越先执行
    /// </summary>
    public int Priority => 100;

    /// <summary>
    /// 是否独占执行
    /// </summary>
    public bool IsExclusive => false;

    /// <summary>
    /// 上一次输出日志的帧序号
    /// </summary>
    private int _lastLogFrameIndex = -1;

    /// <summary>
    /// 触发器日志记录器
    /// </summary>
    private readonly ILogger<SampleTrigger> _logger = App.GetLogger<SampleTrigger>();

    /// <summary>
    /// 从配置同步启用状态
    /// </summary>
    public void Init()
    {
        IsEnabled = ConfigService.Config?.SampleTriggerConfig.Enabled == true;
    }

    /// <summary>
    /// 每隔一段时间输出一次画面信息，证明触发器已经接入调度循环
    /// </summary>
    /// <param name="content">当前帧的截图内容</param>
    public void OnCapture(CaptureContent content)
    {
        // 每 60 帧（约 3 秒）输出一次，避免刷屏
        if (content.FrameIndex == _lastLogFrameIndex || content.FrameIndex % 60 != 0)
        {
            return;
        }

        _lastLogFrameIndex = content.FrameIndex;
        var mat = content.CaptureRectArea.SrcMat;
        _logger.LogInformation("[示例触发器] 帧 {FrameIndex}，画面 {Width}x{Height}",
            content.FrameIndex, mat.Width, mat.Height);
    }
}
