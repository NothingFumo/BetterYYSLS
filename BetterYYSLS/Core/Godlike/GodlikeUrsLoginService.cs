using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BetterYYSLS.Core.Godlike;

/// <summary>
/// URS 扫码登录结果。
/// </summary>
public sealed record GodlikeUrsQrCode(string Uuid, byte[] Image, string Message);

/// <summary>
/// URS 扫码轮询结果。
/// </summary>
public sealed record GodlikeUrsLoginResult(bool IsSuccess, bool IsWaiting, string Message)
{
    /// <summary>
    /// 等待继续轮询。
    /// </summary>
    public static GodlikeUrsLoginResult Waiting(string message) => new(false, true, message);
}

/// <summary>
/// 复刻官方神友 URS 扫码登录的二维码获取与轮询。
/// 只负责官方 URS 认证，不自行生成 GL-Token。
/// </summary>
public sealed class GodlikeUrsLoginService : IDisposable
{
    private const string Product = "godlike_web";
    private const string QrCodeUrl = "https://q.reg.163.com/qrcode/getqrcode/";
    private const string QrCodeIdUrl = "https://q.reg.163.com/qrcode/getqrcodeid/";
    private const string AuthStatusUrl = "https://q.reg.163.com/qrcode/qrcodeauthstatus/";
    private const string AuthUrl = "https://q.reg.163.com/qrcode/qrcodeauth/";
    private const string SetCookieUrl = "https://q.reg.163.com/qrcode/qrcodeSetCookie/";

    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer = new();

    /// <summary>
    /// 当前 URS Cookie。
    /// </summary>
    public CookieContainer Cookies => _cookieContainer;

    /// <summary>
    /// 获取供官方 exchangeTicket 使用的 Cookie 头。
    /// </summary>
    public string GetCookieHeader(Uri uri)
    {
        return _cookieContainer.GetCookieHeader(uri);
    }

    /// <summary>
    /// 创建 URS 登录服务。
    /// </summary>
    public GodlikeUrsLoginService()
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            UseCookies = true
        };
        _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 BetterYYSLS/1.0");
        _httpClient.DefaultRequestHeaders.Referrer = new Uri("https://act.ds.163.com/");
    }

    /// <summary>
    /// 创建二维码。
    /// </summary>
    public async Task<GodlikeUrsQrCode> CreateQrCodeAsync(CancellationToken cancellationToken = default)
    {
        var idResponse = await _httpClient.GetStringAsync(
            $"{QrCodeIdUrl}?product={Product}", cancellationToken);
        var uuid = ParseUuid(idResponse);
        if (string.IsNullOrWhiteSpace(uuid))
        {
            throw new InvalidOperationException("URS 未返回二维码 UUID。");
        }

        var imageUrl = $"{QrCodeUrl}?uuid={Uri.EscapeDataString(uuid)}&size=200" +
                       $"&url={Uri.EscapeDataString("https://ds.163.com")}" +
                       $"&url2={Uri.EscapeDataString("https://ds.163.com")}" +
                       $"&product={Product}";
        var image = await _httpClient.GetByteArrayAsync(imageUrl, cancellationToken);
        return new GodlikeUrsQrCode(uuid, image, "请使用大神 APP 扫码登录");
    }

    /// <summary>
    /// 轮询扫码状态；成功时不生成 GL Token。
    /// </summary>
    public async Task<GodlikeUrsLoginResult> PollAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(uuid))
        {
            return new GodlikeUrsLoginResult(false, false, "二维码 UUID 为空");
        }

        var status = await GetRetCodeAsync(
            $"{AuthStatusUrl}?product={Product}&uuid={Uri.EscapeDataString(uuid)}",
            cancellationToken);
        if (status == "408")
        {
            return GodlikeUrsLoginResult.Waiting("等待扫码...");
        }

        if (status != "409" && status != "200")
        {
            return new GodlikeUrsLoginResult(false, false, $"URS 状态 {status}");
        }

        var authResponse = await _httpClient.GetStringAsync(
            $"{AuthUrl}?product={Product}&uuid={Uri.EscapeDataString(uuid)}&newQrCode=1",
            cancellationToken);
        var authCode = ParseRetCode(authResponse);
        if (authCode == "409")
        {
            return GodlikeUrsLoginResult.Waiting("已扫码，等待确认...");
        }

        if (authCode != "200")
        {
            return new GodlikeUrsLoginResult(false, false, $"URS 认证状态 {authCode}");
        }

        await _httpClient.GetStringAsync(
            $"{SetCookieUrl}?product={Product}&uuid={Uri.EscapeDataString(uuid)}" +
            $"&url={Uri.EscapeDataString("https://ds.163.com")}" +
            $"&url2={Uri.EscapeDataString("https://ds.163.com")}",
            cancellationToken);

        return new GodlikeUrsLoginResult(
            true,
            false,
            "URS 扫码成功。请启动官方客户端完成原生 GL 登录，随后会自动读取登录缓存。");
    }

    private static string? ParseUuid(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(NormalizeJson(text));
            var root = document.RootElement;
            if (root.TryGetProperty("l", out var login) &&
                login.TryGetProperty("i", out var id))
            {
                return id.GetString();
            }

            if (root.TryGetProperty("content", out var content))
            {
                var contentText = content.ValueKind == JsonValueKind.String
                    ? content.GetString()
                    : content.GetRawText();
                if (!string.IsNullOrWhiteSpace(contentText))
                {
                    using var inner = JsonDocument.Parse(NormalizeJson(contentText));
                    if (inner.RootElement.TryGetProperty("l", out var innerLogin) &&
                        innerLogin.TryGetProperty("i", out var innerId))
                    {
                        return innerId.GetString();
                    }
                }
            }
        }
        catch (JsonException)
        {
            // URS 偶尔返回 JSONP/HTML，交给上层显示失败。
        }

        return null;
    }

    private async Task<string> GetRetCodeAsync(string url, CancellationToken cancellationToken)
    {
        var text = await _httpClient.GetStringAsync(url, cancellationToken);
        return ParseRetCode(text);
    }

    private static string ParseRetCode(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(NormalizeJson(text));
            if (document.RootElement.TryGetProperty("retCode", out var code))
            {
                return code.ValueKind == JsonValueKind.String
                    ? code.GetString() ?? string.Empty
                    : code.ToString();
            }
        }
        catch (JsonException)
        {
            // 非 JSON 响应视为失败。
        }

        return "unknown";
    }

    private static string NormalizeJson(string text)
    {
        var start = text.IndexOf('{');
        return start > 0 ? text[start..] : text;
    }

    /// <summary>
    /// 释放 HTTP 客户端。
    /// </summary>
    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
