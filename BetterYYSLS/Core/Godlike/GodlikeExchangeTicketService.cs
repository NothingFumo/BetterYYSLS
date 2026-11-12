using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BetterYYSLS.Core.Godlike;

/// <summary>
/// 官方 exchangeTicket 返回。
/// </summary>
public sealed record GodlikeExchangeResult(
    bool IsSuccess,
    string Message,
    string? Ticket,
    string RawResponse);

/// <summary>
/// 只调用官方 exchangeTicket/byUrsCookie 服务。
/// 不在本地生成或伪造 GL-Token。
/// </summary>
public sealed class GodlikeExchangeTicketService : IDisposable
{
    private const string ExchangeUrl =
        "https://pc.ds.163.com/v1/pc/base/uni/exchangeTicket/byUrsCookie";

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>
    /// 使用 URS Cookie 调用官方票据交换接口。
    /// </summary>
    public async Task<GodlikeExchangeResult> ExchangeAsync(
        string cookieHeader,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            return new GodlikeExchangeResult(false, "缺少 URS Cookie", null, string.Empty);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, ExchangeUrl)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        request.Headers.TryAddWithoutValidation("GL-ClientType", "56");
        request.Headers.TryAddWithoutValidation("GL-Source", "pc_client");
        request.Headers.TryAddWithoutValidation("GL-Version", "1.0.16");
        request.Headers.TryAddWithoutValidation("User-Agent", "GodlikePC Godlike/1.0.16");

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            var code = ReadInt(raw, "code");
            if (code == 200)
            {
                var ticket = ReadString(raw, "ticket") ??
                             ReadString(raw, "token") ??
                             ReadString(raw, "accessToken") ??
                             ReadString(raw, "access_token");
                return new GodlikeExchangeResult(
                    !string.IsNullOrWhiteSpace(ticket),
                    string.IsNullOrWhiteSpace(ticket) ? "官方 exchangeTicket 未返回票据" : "官方 exchangeTicket 成功",
                    ticket,
                    raw);
            }

            var message = ReadString(raw, "errmsg") ?? $"官方 exchangeTicket HTTP {(int)response.StatusCode}";
            return new GodlikeExchangeResult(false, message, null, raw);
        }
        catch (Exception ex)
        {
            return new GodlikeExchangeResult(false, $"官方 exchangeTicket 请求失败：{ex.Message}", null, string.Empty);
        }
    }

    private static int ReadInt(string json, string property)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(property, out var value)
                ? value.GetInt32()
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static string? ReadString(string json, string property)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty(property, out var value))
            {
                return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
            }

            if (document.RootElement.TryGetProperty("result", out var result) &&
                result.ValueKind == JsonValueKind.Object &&
                result.TryGetProperty(property, out var resultValue))
            {
                return resultValue.ValueKind == JsonValueKind.String ? resultValue.GetString() : resultValue.ToString();
            }
        }
        catch (JsonException)
        {
            // 非 JSON 响应不解析。
        }

        return null;
    }

    /// <summary>
    /// 释放 HTTP 客户端。
    /// </summary>
    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
