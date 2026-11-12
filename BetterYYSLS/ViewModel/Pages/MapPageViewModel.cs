using BetterYYSLS.View;
using BetterYYSLS.ViewModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace BetterYYSLS.ViewModel.Pages;

/// <summary>
/// 大地图入口页的视图模型。
/// </summary>
public partial class MapPageViewModel : ObservableObject, IViewModel
{
    private MapWindow _mapWindow;
    private readonly GodlikeLoginWindow _loginWindow;

    /// <summary>
    /// 大地图窗口的数据。
    /// </summary>
    public MapWindowViewModel Map { get; }

    /// <summary>
    /// 当前地图状态。
    /// </summary>
    public string Status => Map.Status;

    /// <summary>
    /// 当前 Godlike 登录态。
    /// </summary>
    public string SessionStatus => Map.SessionStatus;

    /// <summary>
    /// 创建大地图入口页。
    /// </summary>
    public MapPageViewModel(MapWindow mapWindow, MapWindowViewModel map, GodlikeLoginWindow loginWindow)
    {
        _mapWindow = mapWindow;
        _loginWindow = loginWindow;
        Map = map;
        Map.PropertyChanged += OnMapPropertyChanged;
    }

    /// <summary>
    /// 打开地图窗口。
    /// </summary>
    [RelayCommand]
    private void OpenMap()
    {
        try
        {
            _mapWindow.Show();
            _mapWindow.Activate();
        }
        catch (InvalidOperationException)
        {
            // Window 一旦 Close 后不能再 Show，使用同一视图模型重新创建。
            _mapWindow = new MapWindow(Map);
            _mapWindow.Show();
            _mapWindow.Activate();
        }
        catch (Exception ex)
        {
            Map.Status = $"打开大地图失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 打开 URS 扫码登录窗口。
    /// </summary>
    [RelayCommand]
    private void OpenLogin()
    {
        _loginWindow.Show();
        _loginWindow.Activate();
    }

    /// <summary>
    /// 从 Godlike 同步地图数据。
    /// </summary>
    [RelayCommand]
    private async Task SyncGodlike()
    {
        await Map.SyncFromGodlikeAsync();
        OpenMap();
    }

    /// <summary>
    /// 导入地图快照。
    /// </summary>
    [RelayCommand]
    private async Task ImportSnapshot()
    {
        await Map.ImportSnapshotAsync();
        OpenMap();
    }

    /// <summary>
    /// 切换点击穿透。
    /// </summary>
    [RelayCommand]
    private void ToggleClickThrough()
    {
        Map.IsClickThrough = !Map.IsClickThrough;
    }

    /// <summary>
    /// 切换全屏。
    /// </summary>
    [RelayCommand]
    private void ToggleFullScreen()
    {
        Map.IsFullScreen = !Map.IsFullScreen;
    }

    private void OnMapPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapWindowViewModel.Status))
        {
            OnPropertyChanged(nameof(Status));
        }
        else if (e.PropertyName == nameof(MapWindowViewModel.SessionStatus))
        {
            OnPropertyChanged(nameof(SessionStatus));
        }
    }
}
