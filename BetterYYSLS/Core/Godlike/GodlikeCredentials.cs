using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterYYSLS.Core.Godlike;

/// <summary>
/// Godlike 客户端会话头。
/// 只保存在内存中，不写入日志或配置文件。
/// </summary>
public sealed class GodlikeCredentials
{
    /// <summary>
    /// GL-Channel。
    /// </summary>
    [JsonPropertyName("GL-Channel")]
    public string Channel { get; set; } = "official";

    /// <summary>
    /// GL-ClientType。
    /// </summary>
    [JsonPropertyName("GL-ClientType")]
    public string ClientType { get; set; } = "56";

    /// <summary>
    /// GL-DeviceId。
    /// </summary>
    [JsonPropertyName("GL-DeviceId")]
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// GL-Source。
    /// </summary>
    [JsonPropertyName("GL-Source")]
    public string Source { get; set; } = "pc_client";

    /// <summary>
    /// GL-Token。
    /// </summary>
    [JsonPropertyName("GL-Token")]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// GL-Uid。
    /// </summary>
    [JsonPropertyName("GL-Uid")]
    public string Uid { get; set; } = string.Empty;

    /// <summary>
    /// GL-Version。
    /// </summary>
    [JsonPropertyName("GL-Version")]
    public string Version { get; set; } = "1.0.16";

    /// <summary>
    /// 从 Godlike 客户端缓存读取并解密会话。
    /// </summary>
    public static GodlikeCredentials Load()
    {
        var path = FindCachePath();
        if (path is null)
        {
            var primary = GetPrimaryCachePath();
            throw new FileNotFoundException(
                $"未找到 Godlike 登录缓存（{primary}），请先登录官方客户端。",
                primary);
        }

        var text = File.ReadAllText(path).Trim();
        const string prefix = "DPAPIv1:";
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Godlike 登录缓存格式不受支持。");
        }

        var protectedBytes = Convert.FromBase64String(text[prefix.Length..]);
        var plain = Dpapi.Unprotect(protectedBytes);
        var credentials = JsonSerializer.Deserialize<GodlikeCredentials>(plain);
        if (credentials is null || string.IsNullOrWhiteSpace(credentials.Token) ||
            string.IsNullOrWhiteSpace(credentials.Uid))
        {
            throw new InvalidDataException("Godlike 登录缓存中没有有效会话。");
        }

        return credentials;
    }

    /// <summary>
    /// 官方客户端默认缓存路径。
    /// 客户端安装在 D:\GodlikeClient 时，登录态仍由官方客户端写入当前用户的 LocalAppData。
    /// </summary>
    public static string GetPrimaryCachePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ds_client",
            "godlike_info.json");
    }

    /// <summary>
    /// 查找官方客户端登录缓存。
    /// 优先使用官方当前用户缓存；若客户端以绿色版方式在安装目录留下缓存，也支持回退。
    /// </summary>
    public static string? FindCachePath()
    {
        var primary = GetPrimaryCachePath();
        if (File.Exists(primary))
        {
            return primary;
        }

        var installDir = Environment.GetEnvironmentVariable("GODLIKE_CLIENT_DIR");
        if (string.IsNullOrWhiteSpace(installDir))
        {
            installDir = @"D:\GodlikeClient";
        }

        if (!Directory.Exists(installDir))
        {
            return null;
        }

        try
        {
            return Directory
                .EnumerateFiles(installDir, "godlike_info.json", SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Windows DPAPI 解包。
    /// </summary>
    private static class Dpapi
    {
        private const int CryptprotectUiForbidden = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptUnprotectData(
            ref DataBlob dataIn,
            IntPtr description,
            IntPtr entropy,
            IntPtr reserved,
            IntPtr prompt,
            int flags,
            out DataBlob dataOut);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr handle);

        /// <summary>
        /// 解密当前用户的 DPAPI 数据。
        /// </summary>
        public static string Unprotect(byte[] cipher)
        {
            ArgumentNullException.ThrowIfNull(cipher);
            var inputPtr = Marshal.AllocHGlobal(cipher.Length);
            try
            {
                Marshal.Copy(cipher, 0, inputPtr, cipher.Length);
                var input = new DataBlob { Size = cipher.Length, Data = inputPtr };
                if (!CryptUnprotectData(
                        ref input,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        CryptprotectUiForbidden,
                        out var output))
                {
                    throw new InvalidOperationException(
                        $"DPAPI 解密失败，错误码 {Marshal.GetLastWin32Error()}。");
                }

                try
                {
                    var bytes = new byte[output.Size];
                    Marshal.Copy(output.Data, bytes, 0, output.Size);
                    return Encoding.UTF8.GetString(bytes);
                }
                finally
                {
                    LocalFree(output.Data);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(inputPtr);
            }
        }
    }
}
