using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace BetterYYSLS.Core.Map;

/// <summary>
/// 地图快照的 JSON 导入/导出。
/// 文件只包含地图数据，不包含登录凭证或运行时状态。
/// </summary>
public static class MapSnapshotStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// 从文件读取地图快照。
    /// </summary>
    public static async Task<MapSnapshot> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = File.OpenRead(path);
        var snapshot = await JsonSerializer.DeserializeAsync<MapSnapshot>(
            stream,
            SerializerOptions,
            cancellationToken);
        if (snapshot is null)
        {
            throw new InvalidDataException("地图快照文件内容为空。");
        }

        return MapSnapshotNormalizer.Normalize(snapshot);
    }

    /// <summary>
    /// 把地图快照写入文件。
    /// </summary>
    public static async Task SaveAsync(
        string path,
        MapSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(snapshot);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken);
    }
}
