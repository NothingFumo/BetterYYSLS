using BetterYYSLS.Core.Config;
using BetterYYSLS.Service.Interface;
using BetterYYSLS.View.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace BetterYYSLS.ViewModel.Pages;

/// <summary>
/// 通用设置页 ViewModel：遮罩与托盘设置。
/// </summary>
public partial class CommonSettingsPageViewModel : ObservableObject, INavigationAware, IViewModel
{
    /// <summary>
    /// 配置根
    /// </summary>
    public AllConfig Config { get; set; }

    private readonly INavigationService _navigationService;

    /// <summary>
    /// 创建通用设置页 ViewModel
    /// </summary>
    public CommonSettingsPageViewModel(IConfigService configService, INavigationService navigationService)
    {
        Config = configService.Get();
        _navigationService = navigationService;
    }

    public void OnNavigatedTo()
    {
    }

    public void OnNavigatedFrom()
    {
    }

    /// <summary>
    /// 通知遮罩窗口重新计算控件位置
    /// </summary>
    [RelayCommand]
    public void OnRefreshMaskSettings()
    {
        WeakReferenceMessenger.Default.Send(
            new PropertyChangedMessage<object>(this, "RefreshSettings", new object(), "重新计算控件位置"));
    }

    /// <summary>
    /// 跳转到快捷键页
    /// </summary>
    [RelayCommand]
    public void OnGoToHotKeyPage()
    {
        _navigationService.Navigate(typeof(HotKeyPage));
    }
}
