using BetterYYSLS.Core.Config;
using BetterYYSLS.GameTask.Sample;
using BetterYYSLS.View.Drawable;
using OpenCvSharp;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BetterYYSLS.GameTask;

/// <summary>
/// 实时触发器的创建与配置刷新入口。
/// 新增游戏功能时，在 <see cref="LoadTriggers"/> 中注册触发器，并在 <see cref="RefreshTriggerConfigs"/> 中同步配置。
/// </summary>
internal class GameTaskManager
{
    /// <summary>
    /// 当前截图会话中的触发器，按名称索引
    /// </summary>
    public static Dictionary<string, ITaskTrigger>? TriggerDictionary { get; set; }

    /// <summary>
    /// 创建本次截图会话的触发器。必须在任务上下文初始化完毕后调用。
    /// </summary>
    /// <returns>按优先级从高到低排列的触发器</returns>
    public static List<ITaskTrigger> LoadTriggers()
    {
        TriggerDictionary = new Dictionary<string, ITaskTrigger>
        {
            ["Sample"] = new SampleTrigger(),
            ["Placeholder"] = new PlaceholderTrigger(),
        };

        var loadedTriggers = TriggerDictionary.Values.ToList();
        loadedTriggers.ForEach(i => i.Init());

        return loadedTriggers.OrderByDescending(i => i.Priority).ToList();
    }

    /// <summary>
    /// 配置变化后同步触发器的启用状态，并清空遮罩上的识别结果
    /// </summary>
    public static void RefreshTriggerConfigs()
    {
        if (TriggerDictionary is { Count: > 0 })
        {
            TriggerDictionary["Sample"].Init();
            TriggerDictionary["Placeholder"].Init();
            VisionContext.Instance().DrawContent.ClearAll();
        }
    }

    /// <summary>
    /// 重新加载素材。框架本身不包含游戏素材，保留扩展点。
    /// </summary>
    public static void ReloadAssets()
    {
    }

    /// <summary>
    /// 获取素材图片并缩放
    /// </summary>
    /// <param name="featName">任务名称</param>
    /// <param name="assertName">素材文件名</param>
    /// <param name="flags">读取模式</param>
    /// <returns>已按当前游戏分辨率缩放的素材</returns>
    /// <exception cref="FileNotFoundException">素材目录不存在</exception>
    public static Mat LoadAssetImage(string featName, string assertName, ImreadModes flags = ImreadModes.Color)
    {
        var info = TaskContext.Instance().SystemInfo;
        var assetsFolder = Global.Absolute($@"GameTask\{featName}\Assets\{info.GameScreenSize.Width}x{info.GameScreenSize.Height}");
        if (!Directory.Exists(assetsFolder))
        {
            assetsFolder = Global.Absolute($@"GameTask\{featName}\Assets\1920x1080");
        }

        if (!Directory.Exists(assetsFolder))
        {
            throw new FileNotFoundException($"未找到 {featName} 的素材文件夹");
        }

        var mat = Cv2.ImRead(Path.Combine(assetsFolder, assertName), flags);
        if (mat.Empty())
        {
            throw new FileNotFoundException($"未找到素材文件：{assertName}");
        }

        return mat.Resize(new OpenCvSharp.Size(
            (int)(mat.Width * info.AssetScale),
            (int)(mat.Height * info.AssetScale)));
    }
}
