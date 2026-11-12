using BetterYYSLS.ViewModel.Pages;
using System.Windows.Controls;

namespace BetterYYSLS.View.Pages;

/// <summary>
/// 大地图入口页。
/// </summary>
public partial class MapPage : Page
{
    private MapPageViewModel ViewModel { get; }

    /// <summary>
    /// 创建大地图入口页。
    /// </summary>
    public MapPage(MapPageViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}
