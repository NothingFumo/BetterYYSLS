using BetterYYSLS.ViewModel;
using System;
using System.Windows;
using System.Windows.Input;

namespace BetterYYSLS.View;

/// <summary>
/// Godlike URS 扫码登录窗口。
/// </summary>
public partial class GodlikeLoginWindow : Window
{
    private readonly GodlikeLoginWindowViewModel _viewModel;

    /// <summary>
    /// 创建扫码登录窗口。
    /// </summary>
    public GodlikeLoginWindow(GodlikeLoginWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // 拖动过程中窗口状态可能变化。
            }
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Dispose();
    }
}
