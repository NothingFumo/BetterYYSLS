using BetterYYSLS.Core.Config;
using BetterYYSLS.GameTask;
using BetterYYSLS.Model;
using BetterYYSLS.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using HotKeySettingModel = BetterYYSLS.Model.HotKeySettingModel;

namespace BetterYYSLS.ViewModel.Pages;

/// <summary>
/// 快捷键页 ViewModel：框架只保留启动/停止与截图两个快捷键。
/// </summary>
public partial class HotKeyPageViewModel : ObservableObject, IViewModel
{
    private readonly ILogger<HotKeyPageViewModel> _logger;

    /// <summary>
    /// 配置根
    /// </summary>
    public AllConfig Config { get; set; }

    /// <summary>
    /// 页面展示的快捷键配置列表
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<HotKeySettingModel> _hotKeySettingModels = new();

    /// <summary>
    /// 创建快捷键页 ViewModel，并注册全局热键
    /// </summary>
    public HotKeyPageViewModel(IConfigService configService, ILogger<HotKeyPageViewModel> logger)
    {
        _logger = logger;
        Config = configService.Get();

        BuildHotKeySettingModelList();

        foreach (var hotKeyConfig in HotKeySettingModels)
        {
            hotKeyConfig.RegisterHotKey();
            hotKeyConfig.PropertyChanged += (sender, e) =>
            {
                if (sender is not HotKeySettingModel model)
                {
                    return;
                }

                // 快捷键变化时写回配置
                if (e.PropertyName == nameof(HotKeySettingModel.HotKey))
                {
                    var pi = Config.HotKeyConfig.GetType()
                        .GetProperty(model.ConfigPropertyName, BindingFlags.Public | BindingFlags.Instance);
                    if (pi is { CanWrite: true })
                    {
                        var str = model.HotKey.ToString();
                        if (str == "< None >")
                        {
                            str = "";
                        }

                        pi.SetValue(Config.HotKeyConfig, str, null);
                    }
                }

                // 快捷键类型变化时重置快捷键并写回类型
                if (e.PropertyName == nameof(HotKeySettingModel.HotKeyType))
                {
                    model.HotKey = BetterYYSLS.Model.HotKey.None;
                    var pi = Config.HotKeyConfig.GetType()
                        .GetProperty(model.ConfigPropertyName + "Type", BindingFlags.Public | BindingFlags.Instance);
                    if (pi is { CanWrite: true })
                    {
                        pi.SetValue(Config.HotKeyConfig, model.HotKeyType.ToString(), null);
                    }
                }

                RemoveDuplicateHotKey(model);
                model.UnRegisterHotKey();
                model.RegisterHotKey();
                Debug.WriteLine($"{model.FunctionName} 快捷键已更新为 {model.HotKey}");
            };
        }
    }

    /// <summary>
    /// 移除与其他功能重复的快捷键
    /// </summary>
    private void RemoveDuplicateHotKey(HotKeySettingModel current)
    {
        if (current.HotKey.IsEmpty)
        {
            return;
        }

        foreach (var item in HotKeySettingModels)
        {
            if (item.HotKey.IsEmpty)
            {
                continue;
            }

            if (item.ConfigPropertyName != current.ConfigPropertyName && item.HotKey == current.HotKey)
            {
                item.HotKey = BetterYYSLS.Model.HotKey.None;
            }
        }
    }

    /// <summary>
    /// 构建快捷键配置列表
    /// </summary>
    private void BuildHotKeySettingModelList()
    {
        HotKeySettingModels.Add(new HotKeySettingModel(
            "启动/停止实时触发器",
            nameof(Config.HotKeyConfig.StartStopHotkey),
            Config.HotKeyConfig.StartStopHotkey,
            Config.HotKeyConfig.StartStopHotkeyType,
            (_, _) => WeakReferenceMessenger.Default.Send(
                new PropertyChangedMessage<object>(this, "SwitchTriggerStatus", "", ""))
        ));

        HotKeySettingModels.Add(new HotKeySettingModel(
            "保存当前画面截图",
            nameof(Config.HotKeyConfig.TakeScreenshotHotkey),
            Config.HotKeyConfig.TakeScreenshotHotkey,
            Config.HotKeyConfig.TakeScreenshotHotkeyType,
            (_, _) =>
            {
                try
                {
                    TaskTriggerDispatcher.Instance().TakeScreenshot();
                }
                catch (System.Exception ex)
                {
                    _logger.LogWarning("截图失败：{Message}", ex.Message);
                }
            }
        ));

        _logger.LogInformation("已加载 {Count} 个快捷键配置", HotKeySettingModels.Count);
    }
}

