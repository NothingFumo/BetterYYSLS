using System;
using Microsoft.Data.Sqlite;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using System.Text.Json;

namespace BetterYYSLS.Core.Godlike;

/// <summary>
/// Godlike 当前账号绑定的游戏角色。
/// 角色 ID 与账号 UID 是两个不同的标识。
/// </summary>
public sealed class GodlikeRole
{
    /// <summary>
    /// 角色 ID，例如 0406533855。
    /// </summary>
    public string RoleId { get; set; } = string.Empty;

    /// <summary>
    /// 角色昵称。
    /// </summary>
    public string Nick { get; set; } = string.Empty;

    /// <summary>
    /// 角色等级。
    /// </summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>
    /// 服务器编号，例如 10040。
    /// </summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>
    /// 服务器名称。
    /// </summary>
    public string ServerName { get; set; } = string.Empty;

    /// <summary>
    /// 游戏 appKey。
    /// </summary>
    public string AppKey { get; set; } = string.Empty;

    /// <summary>
    /// 角色所属账号 UID。
    /// </summary>
    public string Uid { get; set; } = string.Empty;

    /// <summary>
    /// 角色头像。
    /// </summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// 下拉框显示文本。
    /// </summary>
    public string DisplayText => string.IsNullOrWhiteSpace(Nick)
        ? $"角色 {RoleId} · {ServerName}"
        : $"{Nick} · Lv.{Level} · {ServerName}";
}

/// <summary>
/// 读取 Godlike 角色绑定列表；只读，不修改账号绑定关系。
/// </summary>
public static class GodlikeRoleService
{
    private const string RoleBaseUrl = "https://pc-role.ds.163.com";
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>
    /// 查询当前 Godlike 账号绑定的燕云角色。
    /// </summary>
    public static async Task<IReadOnlyList<GodlikeRole>> LoadRolesAsync(
        GodlikeCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            RoleBaseUrl + "/v1/pc/role/getBindList")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        };
        AddHeaders(request, credentials);

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Godlike 角色接口返回 {(int)response.StatusCode}。");
        }

        using var document = JsonDocument.Parse(text);
        if (!document.RootElement.TryGetProperty("result", out var result))
        {
            return [];
        }

        var roles = EnumerateRoles(result)
            .Where(role => string.IsNullOrWhiteSpace(role.AppKey) ||
                           role.AppKey.Equals("h72", StringComparison.OrdinalIgnoreCase))
            .GroupBy(role => role.RoleId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        return roles;
    }

    /// <summary>
    /// 按角色 ID 查找服务器；先查绑定列表和客户端缓存，最后从 CEF 日志的会话记录补全。
    /// </summary>
    public static async Task<GodlikeRole?> FindRoleAsync(
        GodlikeCredentials credentials,
        string roleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        if (string.IsNullOrWhiteSpace(roleId))
        {
            return null;
        }

        roleId = roleId.Trim();
        try
        {
            var roles = await LoadRolesAsync(credentials, cancellationToken);
            var found = roles.FirstOrDefault(role => role.RoleId == roleId);
            if (found is not null)
            {
                return found;
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // 绑定列表需要签名，继续使用本地缓存和日志。
        }

        var cached = LoadCachedRoles(credentials);
        var cachedFound = cached.FirstOrDefault(role => role.RoleId == roleId);
        if (cachedFound is not null)
        {
            return cachedFound;
        }

        // 会话日志中会记录角色消息的 fromId/fromServer，可用于补全任意已交互角色。
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ds_client",
                "logs");
            if (!Directory.Exists(logDirectory))
            {
                return null;
            }

            foreach (var file in Directory.GetFiles(logDirectory, "ds_cef_*.log")
                         .OrderByDescending(File.GetLastWriteTimeUtc)
                         .Take(8))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                var length = (int)Math.Min(stream.Length, 8 * 1024 * 1024);
                stream.Seek(-length, SeekOrigin.End);
                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                var text = reader.ReadToEnd();
                var index = text.LastIndexOf($"\"roleId\":\"{roleId}\"", StringComparison.Ordinal);
                if (index < 0)
                {
                    index = text.LastIndexOf($"\"fromId\":\"{roleId}\"", StringComparison.Ordinal);
                }

                if (index < 0)
                {
                    continue;
                }

                var start = Math.Max(0, index - 600);
                var end = Math.Min(text.Length, index + 600);
                var window = text[start..end];
                var relativeIndex = index - start;
                var serverMatches = Regex.Matches(
                    window,
                    "\"(?:server|fromServer)\":\"(?<server>\\d+)\"");
                var serverMatch = serverMatches
                    .Cast<Match>()
                    .OrderBy(match => Math.Abs(match.Index - relativeIndex))
                    .FirstOrDefault();
                if (serverMatch is null)
                {
                    continue;
                }

                // 日志里如果附近还带有昵称、头像和服务器名，一并补齐，
                // 避免手动输入的角色永远只显示角色 ID。
                var nickMatch = Regex.Match(window, "\"nick\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"");
                var iconMatch = Regex.Match(window, "\"icon\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"");
                var serverNameMatch = Regex.Match(window, "\"serverName\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"");
                return new GodlikeRole
                {
                    RoleId = roleId,
                    Server = serverMatch.Groups["server"].Value,
                    ServerName = serverNameMatch.Success
                        ? UnescapeLogString(serverNameMatch.Groups["value"].Value)
                        : "正式服",
                    Nick = nickMatch.Success
                        ? UnescapeLogString(nickMatch.Groups["value"].Value)
                        : string.Empty,
                    Icon = iconMatch.Success
                        ? UnescapeLogString(iconMatch.Groups["value"].Value)
                        : string.Empty,
                    AppKey = "h72"
                };
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // 日志解析失败时返回 null，由界面提示手动填写服务器。
        }

        return null;
    }

    /// <summary>
    /// 从官方客户端 CEF 日志中的 dsPc_getMyGameRoleList 读取完整角色列表。
    /// getBindList 需要 CEF 签名并可能返回 868，客户端内存快照可作为只读回退。
    /// </summary>
    public static IReadOnlyList<GodlikeRole> LoadCachedRoles(GodlikeCredentials credentials)
    {
        var roles = new List<GodlikeRole>();
        var maskedRoles = new List<GodlikeRole>();
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ds_client",
                "logs");
            if (!Directory.Exists(logDirectory))
            {
                return roles;
            }

            foreach (var file in Directory.GetFiles(logDirectory, "ds_cef_*.log")
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                try
                {
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    var length = (int)Math.Min(stream.Length, 4 * 1024 * 1024);
                    stream.Seek(-length, SeekOrigin.End);
                    using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                    var text = reader.ReadToEnd();
                    var marker = "\"key\":\"dsPc_getMyGameRoleList\",\"value\":";
                    var index = text.LastIndexOf(marker, StringComparison.Ordinal);
                    if (index < 0)
                    {
                        continue;
                    }

                    var start = text.IndexOf('{', index + marker.Length);
                    if (start < 0 || !TryExtractJsonObject(text, start, out var json))
                    {
                        continue;
                    }

                    using var document = JsonDocument.Parse(json);
                    if (!document.RootElement.TryGetProperty("list", out var list) ||
                        list.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var item in list.EnumerateArray())
                    {
                        if (TryReadRole(item, out var role) &&
                            (string.IsNullOrWhiteSpace(role.AppKey) ||
                             role.AppKey.Equals("h72", StringComparison.OrdinalIgnoreCase)))
                        {
                            roles.Add(role);
                        }
                    }

                    // 部分新版客户端日志会把 roleId 脱敏成 "***"；
                    // 记录昵称/服务器，稍后从客户端角色数据库还原真实 ID。
                    if (roles.Count > 0 && roles.All(role => IsUsableRoleId(role.RoleId)))
                    {
                        break;
                    }

                    foreach (var role in roles.Where(role => !IsUsableRoleId(role.RoleId)))
                    {
                        if (!maskedRoles.Any(item =>
                                item.Nick == role.Nick && item.Server == role.Server))
                        {
                            maskedRoles.Add(role);
                        }
                    }

                    roles.Clear();
                }
                catch
                {
                    // 当前客户端可能独占某个日志文件；跳过它继续读取其他日志。
                    continue;
                }
            }
        }
        catch
        {
            // 日志被占用或格式变化时继续尝试当前客户端缓存。
        }

        if (roles.Count == 0 && maskedRoles.Count > 0)
        {
            // 新版日志只保留昵称/服务器；从 im_<uid>.db 的 game_role 表还原真实角色 ID。
            roles.AddRange(LoadRolesFromImCache(credentials, maskedRoles));
        }

        var current = LoadCachedRole(credentials);
        if (current is not null && IsUsableRoleId(current.RoleId))
        {
            roles.RemoveAll(role => role.RoleId == current.RoleId);
            roles.Insert(0, current);
        }

        return roles
            .Where(role => IsUsableRoleId(role.RoleId))
            .GroupBy(role => role.RoleId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    private static bool TryExtractJsonObject(string text, int start, out string json)
    {
        json = string.Empty;
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                }
                continue;
            }

            if (ch == '"')
            {
                inString = true;
            }
            else if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    json = text[start..(i + 1)];
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 从官方客户端本地缓存读取当前角色；角色列表接口需要 CEF 签名，
    /// 接口返回 868 时至少保留当前客户端正在使用的角色。
    /// </summary>
    public static GodlikeRole? LoadCachedRole(GodlikeCredentials credentials)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ds_client",
                "users",
                credentials.Uid,
                "user_store.json");
            if (!File.Exists(path))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("", out var root) ||
                !root.TryGetProperty("dsPc_currentGameRoleInfo", out var infos) ||
                !infos.TryGetProperty("h72", out var role))
            {
                return null;
            }

            return TryReadRole(role, out var cached) ? cached : null;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<GodlikeRole> EnumerateRoles(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in result.EnumerateArray())
            {
                if (TryReadRole(item, out var role))
                {
                    yield return role;
                }
            }

            yield break;
        }

        if (result.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var property in new[] { "list", "roleList", "roles" })
        {
            if (!result.TryGetProperty(property, out var list) ||
                list.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in list.EnumerateArray())
            {
                if (TryReadRole(item, out var role))
                {
                    yield return role;
                }
            }

            yield break;
        }

        if (TryReadRole(result, out var single))
        {
            yield return single;
        }
    }

    /// <summary>
    /// 从官方客户端 IM 数据库还原脱敏日志中的角色 ID。
    /// </summary>
    private static IReadOnlyList<GodlikeRole> LoadRolesFromImCache(
        GodlikeCredentials credentials,
        IReadOnlyList<GodlikeRole> maskedRoles)
    {
        var result = new List<GodlikeRole>();
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ds_client",
            $"im_{credentials.Uid}.db");
        if (!File.Exists(path) || string.IsNullOrWhiteSpace(credentials.Uid))
        {
            return result;
        }

        try
        {
            using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Mode = SqliteOpenMode.ReadOnly,
                    Cache = SqliteCacheMode.Shared
                }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "select roleName, server, roleId, payload from game_role where appKey = 'h72'";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var nick = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var server = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                if (maskedRoles.Any(masked =>
                        string.Equals(masked.Nick, nick, StringComparison.Ordinal) &&
                        (string.IsNullOrWhiteSpace(masked.Server) ||
                         string.Equals(masked.Server, server, StringComparison.Ordinal))))
                {
                    var role = new GodlikeRole
                    {
                        RoleId = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        Server = server,
                        Nick = nick,
                        AppKey = "h72"
                    };
                    if (!reader.IsDBNull(3))
                    {
                        try
                        {
                            using var payload = JsonDocument.Parse(reader.GetString(3));
                            var root = payload.RootElement;
                            if (root.TryGetProperty("icon", out var icon))
                            {
                                role.Icon = icon.GetString() ?? string.Empty;
                            }

                            if (root.TryGetProperty("serverName", out var serverName))
                            {
                                role.ServerName = serverName.GetString() ?? string.Empty;
                            }
                        }
                        catch
                        {
                            // 角色数据库中的 payload 损坏时仍保留基础字段。
                        }
                    }

                    if (IsUsableRoleId(role.RoleId))
                    {
                        result.Add(role);
                    }
                }
            }
        }
        catch
        {
            // 数据库被客户端占用或结构变化时使用现有缓存。
        }

        return result;
    }

    /// <summary>
    /// 判断角色 ID 是否可用于绑定、去重和完成度查询；脱敏日志里的 *** 不能使用。
    /// </summary>
    public static bool IsUsableRoleId(string? roleId)
    {
        return !string.IsNullOrWhiteSpace(roleId) &&
               !roleId.Contains('*', StringComparison.Ordinal) &&
               roleId.Any(char.IsLetterOrDigit);
    }

    private static bool TryReadRole(JsonElement element, out GodlikeRole role)
    {
        role = new GodlikeRole();
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        role.RoleId = ReadString(element, "roleId");
        if (string.IsNullOrWhiteSpace(role.RoleId))
        {
            return false;
        }

        role.Nick = ReadString(element, "nick");
        role.Level = ReadString(element, "level");
        role.Server = ReadString(element, "server");
        role.ServerName = ReadString(element, "serverName");
        role.AppKey = ReadString(element, "appKey");
        role.Uid = ReadString(element, "uid");
        role.Icon = ReadString(element, "icon");
        return true;
    }

    private static string ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.ToString(),
                _ => string.Empty
            }
            : string.Empty;
    }

    /// <summary>
    /// 还原日志 JSON 字符串中的转义字符；失败时保留原值。
    /// </summary>
    private static string UnescapeLogString(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<string>($"\"{value}\"") ?? value;
        }
        catch
        {
            return value;
        }
    }

    private static void AddHeaders(HttpRequestMessage request, GodlikeCredentials credentials)
    {
        var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "_" +
                    Guid.NewGuid().ToString().ToUpperInvariant();
        request.Headers.TryAddWithoutValidation("GL-Channel", credentials.Channel);
        request.Headers.TryAddWithoutValidation("GL-ClientType", credentials.ClientType);
        request.Headers.TryAddWithoutValidation("GL-DeviceId", credentials.DeviceId);
        request.Headers.TryAddWithoutValidation("GL-Source", credentials.Source);
        request.Headers.TryAddWithoutValidation("GL-Token", credentials.Token);
        request.Headers.TryAddWithoutValidation("GL-Uid", credentials.Uid);
        request.Headers.TryAddWithoutValidation("GL-Version", credentials.Version);
        request.Headers.TryAddWithoutValidation("GL-Nonce", nonce);
        request.Headers.TryAddWithoutValidation("User-Agent", "BetterYYSLS/1.0");
    }
}
