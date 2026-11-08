using BetterYYSLS.Core.Config;
using BetterYYSLS.Service.Interface;
using BetterYYSLS.View.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace BetterYYSLS.ViewModel.Pages;

/// <summary>
/// 实时触发页 ViewModel：示例触发器与占位触发器的开关。
/// </summary>
public partial class TriggerSettingsPageViewModel : ObservableObject, INavigationAware, IViewModel
{
    /// <summary>
    /// 配置根
    /// </summary>
    public AllConfig Config { get; set; }

    private readonly INavigationService _navigationService;

    /// <summary>
    /// 创建实时触发页 ViewModel
    /// </summary>
    public TriggerSettingsPageViewModel(IConfigService configService, INavigationService navigationService)
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
    /// 跳转到快捷键页
    /// </summary>
    [RelayCommand]
    public void OnGoToHotKeyPage()
    {
        _navigationService.Navigate(typeof(HotKeyPage));
    }
}
