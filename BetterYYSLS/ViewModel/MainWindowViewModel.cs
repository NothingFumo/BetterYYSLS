using BetterYYSLS.Core.Config;
using BetterYYSLS.Helpers;
using BetterYYSLS.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Windows;
using Wpf.Ui;

namespace BetterYYSLS.ViewModel;

/// <summary>
/// 主窗口 ViewModel：标题、显隐、退出到托盘。
/// </summary>
public partial class MainWindowViewModel : ObservableObject, IViewModel
{
    private readonly ILogger<MainWindowViewModel> _logger;

    /// <summary>
    /// 窗口标题
    /// </summary>
    public string Title => $"BetterYYSLS · 更好的燕云十六声 · {Global.Version}";

    /// <summary>
    /// 窗口是否可见
    /// </summary>
    [ObservableProperty]
    public bool _isVisible = true;

    /// <summary>
    /// 窗口状态
    /// </summary>
    [ObservableProperty]
    public WindowState _windowState = WindowState.Normal;

    /// <summary>
    /// 配置根
    /// </summary>
    public AllConfig Config { get; set; }

    /// <summary>
    /// 创建主窗口 ViewModel
    /// </summary>
    public MainWindowViewModel(INavigationService navigationService, IConfigService configService)
    {
        Config = configService.Get();
        _logger = App.GetLogger<MainWindowViewModel>();
    }

    /// <summary>
    /// 隐藏到托盘
    /// </summary>
    [RelayCommand]
    private void OnHide()
    {
        IsVisible = false;
    }

    /// <summary>
    /// 关闭时按配置最小化到托盘
    /// </summary>
    [RelayCommand]
    private void OnClosing(CancelEventArgs e)
    {
        if (Config.CommonConfig.ExitToTray)
        {
            e.Cancel = true;
            OnHide();
        }
    }

    /// <summary>
    /// 窗口加载完成
    /// </summary>
    [RelayCommand]
    private void OnLoaded()
    {
        _logger.LogInformation("更好的燕云十六声 {Version}", Global.Version);
    }
}
