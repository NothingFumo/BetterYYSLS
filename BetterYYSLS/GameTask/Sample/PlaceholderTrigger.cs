using BetterYYSLS.Core.Config;
using BetterYYSLS.Service;

namespace BetterYYSLS.GameTask.Sample;

/// <summary>
/// 占位触发器：接入具体游戏功能时从本文件开始实现。
/// 默认不启用，也不会执行任何操作。
/// </summary>
public class PlaceholderTrigger : ITaskTrigger
{
    /// <summary>
    /// 触发器名称
    /// </summary>
    public string Name => "占位触发器";

    /// <summary>
    /// 是否启用，由占位触发器配置控制
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// 执行优先级
    /// </summary>
    public int Priority => 0;

    /// <summary>
    /// 是否独占执行
    /// </summary>
    public bool IsExclusive => false;

    /// <summary>
    /// 从配置同步启用状态
    /// </summary>
    public void Init()
    {
        IsEnabled = ConfigService.Config?.PlaceholderTriggerConfig.Enabled == true;
    }

    /// <summary>
    /// 每帧回调。在此实现识别、判断和模拟输入。
    /// </summary>
    /// <param name="content">当前帧的截图内容</param>
    public void OnCapture(CaptureContent content)
    {
        // TODO: 在此接入具体游戏逻辑
    }
}
