using BetterYYSLS.Core.Godlike;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace BetterYYSLS.ViewModel;

/// <summary>
/// 应用内 URS 扫码登录窗口。
/// </summary>
public partial class GodlikeLoginWindowViewModel : ObservableObject, IDisposable
{
    private readonly GodlikeUrsLoginService _loginService = new();
    private readonly GodlikeExchangeTicketService _exchangeService = new();
    private CancellationTokenSource? _pollCts;

    /// <summary>
    /// 当前二维码。
    /// </summary>
    [ObservableProperty]
    private BitmapImage? _qrImage;

    /// <summary>
    /// 当前状态。
    /// </summary>
    [ObservableProperty]
    private string _status = "正在获取二维码...";

    /// <summary>
    /// 是否正在工作。
    /// </summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// 当前二维码 UUID。
    /// </summary>
    private string? _uuid;

    /// <summary>
    /// 初始化二维码。
    /// </summary>
    public async Task InitializeAsync()
    {
        await RefreshQrCodeAsync();
    }

    /// <summary>
    /// 刷新 URS 二维码。
    /// </summary>
    [RelayCommand]
    private async Task RefreshQrCodeAsync()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = new CancellationTokenSource();
        IsBusy = true;
        Status = "正在获取 URS 二维码...";

        try
        {
            var qrCode = await _loginService.CreateQrCodeAsync(_pollCts.Token);
            _uuid = qrCode.Uuid;
            QrImage = CreateImage(qrCode.Image);
            Status = qrCode.Message;
            _ = PollAsync(_pollCts.Token);
        }
        catch (Exception ex)
        {
            Status = $"获取二维码失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 启动官方客户端，由官方客户端完成原生 GL 登录。
    /// </summary>
    [RelayCommand]
    private void OpenOfficialClient()
    {
        var path = Environment.GetEnvironmentVariable("GODLIKE_CLIENT_DIR");
        if (string.IsNullOrWhiteSpace(path))
        {
            path = @"D:\GodlikeClient";
        }

        var executable = Path.Combine(path, "Start.exe");
        if (!File.Exists(executable))
        {
            Status = $"未找到官方客户端：{executable}";
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WorkingDirectory = path
            });
            Status = "已启动官方客户端。请在官方客户端完成扫码登录，登录后地图会自动刷新。";
        }
        catch (Exception ex)
        {
            Status = $"启动官方客户端失败：{ex.Message}";
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !string.IsNullOrWhiteSpace(_uuid))
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                var result = await _loginService.PollAsync(_uuid, cancellationToken);
                Status = result.Message;
                if (result.IsSuccess)
                {
                    var cookie = _loginService.GetCookieHeader(new Uri("https://pc.ds.163.com/"));
                    var exchange = await _exchangeService.ExchangeAsync(cookie, cancellationToken);
                    Status = exchange.IsSuccess
                        ? "官方 exchangeTicket 成功。请启动官方客户端完成原生登录。"
                        : exchange.Message;
                    return;
                }

                if (!result.IsWaiting)
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Status = $"扫码状态查询失败：{ex.Message}";
                return;
            }
        }
    }

    private static BitmapImage CreateImage(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>
    /// 释放轮询资源。
    /// </summary>
    public void Dispose()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _loginService.Dispose();
        _exchangeService.Dispose();
    }
}
