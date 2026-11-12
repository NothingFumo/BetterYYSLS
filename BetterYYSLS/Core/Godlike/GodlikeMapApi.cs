using BetterYYSLS.Core.Map;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BetterYYSLS.Core.Godlike;

/// <summary>
/// Godlike 地图数据接口。
/// 使用本机 Godlike 客户端留下的 GL 会话，不保存密码。
/// </summary>
public static class GodlikeMapApi
{
    /// <summary>
    /// 当前客户端的燕云地图 ID。
    /// </summary>
    public const string DefaultMapId = "676d48a37d299d0811946ff1";

    private const string BaseUrl = "https://god.gameyw.netease.com";
    private const string WebBaseUrl = "https://inf.ds.163.com";
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 查询网页端当前地图 ID。
    /// 网页地图只暴露当前地图，区域由 anchor/list 给出。
    /// </summary>
    public static async Task<string?> GetCurrentMapIdAsync(
        GodlikeCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        var result = await PostAsync<GodlikeMapInfoResult>(
            credentials,
            "/v1/web/game-map/map-info/get",
            new { appKey = "h72" },
            cancellationToken,
            WebBaseUrl);
        return result.Result?.MapId;
    }

    /// <summary>
    /// 读取官方客户端当前的游戏坐标偏移。
    /// 该接口由原生客户端签名；失败时调用方应使用内置回退值。
    /// </summary>
    public static async Task<GodlikeClientMeta?> GetClientMetaAsync(
        GodlikeCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        var result = await PostAsync<GodlikeClientMetaResult>(
            credentials,
            "/v1/game-map/client-meta/query",
            new { key = "H72_MAP", version = credentials.Version },
            cancellationToken);
        var meta = result.Metas
            .FirstOrDefault(item => string.Equals(item.Key, "H72_MAP", StringComparison.Ordinal));
        if (meta is null || string.IsNullOrWhiteSpace(meta.Value))
        {
            return null;
        }

        var offsets = JsonSerializer.Deserialize<GodlikeClientOffsets>(meta.Value, JsonOptions);
        if (offsets?.OffSet is null ||
            !TryParseOffset(offsets.OffSet.X, out var x) ||
            !TryParseOffset(offsets.OffSet.Y, out var y) ||
            !TryParseOffset(offsets.OffSet.Z, out var z))
        {
            return null;
        }

        return new GodlikeClientMeta
        {
            Version = offsets.CurVersion ?? string.Empty,
            OffsetX = x,
            OffsetY = y,
            OffsetZ = z
        };
    }

    /// <summary>
    /// 读取地图标记详情。
    /// </summary>
    public static async Task<IReadOnlyList<MapMarkerDetails>> LoadPointDetailsAsync(
        GodlikeCredentials credentials,
        string mapId,
        IReadOnlyList<string> pointIds,
        string? roleId = null,
        string? server = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
        if (pointIds.Count == 0)
        {
            return [];
        }

        var result = await PostAsync<List<GodlikePointDetail>>(
            credentials,
            "/v1/web/game-map/point/details",
            new
            {
                mapId,
                pointIds,
                roleId = roleId ?? string.Empty,
                server = server ?? string.Empty,
                appKey = "h72"
            },
            cancellationToken,
            WebBaseUrl);
        return result.Select(detail => new MapMarkerDetails
        {
            PointId = detail.PointId,
            Name = detail.Name,
            Description = detail.Desc,
            CityName = detail.CityName,
            Icon = detail.Icon,
            DescriptionIcon = detail.DescIcon,
            LastSeriesName = detail.LastSeriesName,
            MapSubType = detail.MapSubType,
            Favorite = detail.Favorite,
            Visit = detail.Visit,
            RoleVisit = detail.RoleVisit,
            PointSourceType = detail.PointSourceType,
            RegionId = detail.RegionId,
            TotalCommitterNum = detail.TotalCommitterNum,
            Committers = (detail.Committers ?? [])
                .Select(committer => committer.Name ?? string.Empty)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList()
        }).ToList();
    }

    /// <summary>
    /// 加载路线、快捷入口、收藏、KOL、UGC 等附加信息摘要。
    /// 需要原生签名的接口失败时只记录错误，不阻断其他信息。
    /// </summary>
    public static async Task<string> LoadAuxiliaryInfoAsync(
        GodlikeCredentials credentials,
        string mapId,
        string searchText = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);

        var lines = new List<string>();
        try
        {
            var route = await PostAsync<JsonElement>(
                credentials,
                "/v1/web/game-map/point-route/list",
                new { mapId },
                cancellationToken,
                WebBaseUrl);
            var routeCount = route.ValueKind == JsonValueKind.Object &&
                             route.TryGetProperty("pointRouteDetailList", out var routeList) &&
                             routeList.ValueKind == JsonValueKind.Array
                ? routeList.GetArrayLength()
                : 0;
            lines.Add($"路线 {routeCount}");
        }
        catch (Exception ex)
        {
            lines.Add($"路线失败：{ex.Message}");
        }

        try
        {
            var entries = await PostAsync<JsonElement>(
                credentials,
                "/v1/web/game-map/fast-entry/get-all",
                new { mapId },
                cancellationToken,
                WebBaseUrl);
            var count = entries.ValueKind == JsonValueKind.Array ? entries.GetArrayLength() : 0;
            lines.Add($"快捷入口 {count}");
        }
        catch (Exception ex)
        {
            lines.Add($"快捷入口失败：{ex.Message}");
        }

        try
        {
            var kol = await PostAsync<JsonElement>(
                credentials,
                "/v1/web/game-map/point-series/kol/all-list",
                new { mapId },
                cancellationToken,
                WebBaseUrl);
            var count = kol.ValueKind == JsonValueKind.Object &&
                        kol.TryGetProperty("pointSeriesList", out var series) &&
                        series.ValueKind == JsonValueKind.Array
                ? series.GetArrayLength()
                : 0;
            lines.Add($"KOL 分类 {count}");
        }
        catch (Exception ex)
        {
            lines.Add($"KOL 失败：{ex.Message}");
        }

        try
        {
            var favorites = await PostAsync<JsonElement>(
                credentials,
                "/v1/web/game-map/ugc/favorite-list",
                new { mapId },
                cancellationToken,
                WebBaseUrl);
            var count = favorites.ValueKind == JsonValueKind.Array ? favorites.GetArrayLength() : 0;
            lines.Add($"UGC 收藏 {count}");
        }
        catch
        {
            lines.Add("UGC 收藏需原生签名");
        }

        try
        {
            var visits = await PostAsync<JsonElement>(
                credentials,
                "/v1/web/game-map/ugc/visit-list",
                new { mapId },
                cancellationToken,
                WebBaseUrl);
            var count = visits.ValueKind == JsonValueKind.Array ? visits.GetArrayLength() : 0;
            lines.Add($"访问记录 {count}");
        }
        catch
        {
            lines.Add("访问记录需原生签名");
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            try
            {
                var found = PostAsync<List<GodlikePoint>>(
                    credentials,
                    "/v1/web/game-map/point/search-by-name",
                    new { mapId, name = searchText, layer = "0" },
                    cancellationToken,
                    WebBaseUrl);
                var points = await found;
                lines.Add($"官方搜索 {points.Count}");
            }
            catch (Exception ex)
            {
                lines.Add($"官方搜索失败：{ex.Message}");
            }
        }

        return string.Join(" · ", lines);
    }

    private static bool TryParseOffset(string? value, out ulong offset)
    {
        offset = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? value[2..]
            : value;
        return ulong.TryParse(text, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out offset);
    }

    /// <summary>
    /// 读取当前地图的全部标记与分类，转换为本地快照。
    /// </summary>
    public static async Task<MapSnapshot> LoadSnapshotAsync(
        GodlikeCredentials credentials,
        string mapId = DefaultMapId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);

        try
        {
            var currentMapId = await GetCurrentMapIdAsync(credentials, cancellationToken);
            if (!string.IsNullOrWhiteSpace(currentMapId))
            {
                mapId = currentMapId;
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // 网页接口不可用时继续使用已知 mapId，避免阻断已登录会话的同步。
        }

        var seriesTask = PostAsync<GodlikePointSeriesResult>(
            credentials,
            "/v1/game-map/point-series/list",
            new { mapId },
            cancellationToken);
        var pointsTask = LoadPointsBySubTypesAsync(credentials, mapId, cancellationToken);
        var anchorsTask = PostAsync<List<GodlikeAnchor>>(
            credentials,
            "/v1/web/game-map/anchor/list",
            new { mapId },
            cancellationToken,
            WebBaseUrl);

        await Task.WhenAll(seriesTask, pointsTask, anchorsTask);
        var series = await seriesTask;
        var points = await pointsTask;
        var anchors = await anchorsTask;
        return BuildSnapshot(mapId, series.PointSeriesList, points, anchors);
    }

    /// <summary>
    /// 按 mapZone 和 mapSubType 拉取全部点位。新版网页使用该接口，
    /// 未央城等新区域的点位仍可能标记为 mapSubType=1，因此不能只按 subtype 过滤。
    /// </summary>
    private static async Task<IReadOnlyList<GodlikePoint>> LoadPointsBySubTypesAsync(
        GodlikeCredentials credentials,
        string mapId,
        CancellationToken cancellationToken)
    {
        var zones = new[]
        {
            (Zone: "map12", SubTypes: new[] { "1", "2", "5" }),
            (Zone: "map3", SubTypes: new[] { "3" }),
            (Zone: "map4", SubTypes: new[] { "4" })
        };
        try
        {
            var tasks = zones.Select(zone => PostAsync<List<GodlikePoint>>(
                credentials,
                "/v1/web/game-map/point/list-by-sub-types",
                new { mapId, subTypes = zone.SubTypes, mapZone = zone.Zone },
                cancellationToken,
                WebBaseUrl)).ToArray();
            var batches = await Task.WhenAll(tasks);
            var newPoints = batches
                .SelectMany(batch => batch)
                .GroupBy(point => point.PointId, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
            // 新旧接口各有少量点位：合并后保留全部有效数据。
            var legacyPoints = await PostAsync<List<GodlikePoint>>(
                credentials,
                "/v1/game-map/point/list",
                new { mapId },
                cancellationToken);
            return legacyPoints
                .Concat(newPoints)
                .GroupBy(point => point.PointId, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // 新接口不可用时回退到旧接口，避免已有登录态完全无法同步。
        }

        return await PostAsync<List<GodlikePoint>>(
            credentials,
            "/v1/game-map/point/list",
            new { mapId },
            cancellationToken);
    }

    private static MapSnapshot BuildSnapshot(
        string mapId,
        IReadOnlyList<GodlikePointSeries> sourceSeries,
        IReadOnlyList<GodlikePoint> points,
        IReadOnlyList<GodlikeAnchor> anchors)
    {
        var snapshot = new MapSnapshot
        {
            MapId = mapId,
            Name = "燕云十六声",
            Version = "live"
        };

        var categories = new List<MapCategory>();
        var categoryIds = new HashSet<string>(StringComparer.Ordinal);
        var order = 0;
        void AddSeries(GodlikePointSeries source, string? parentId = null)
        {
            if (string.IsNullOrWhiteSpace(source.SeriesId) || !categoryIds.Add(source.SeriesId))
            {
                return;
            }

            categories.Add(new MapCategory
            {
                Id = source.SeriesId,
                ParentId = parentId,
                Name = source.SeriesName,
                Icon = source.PosterUrl,
                Order = order++,
                IsVisibleByDefault = source.DefaultShow
            });

            foreach (var child in source.Children ?? [])
            {
                AddSeries(child, source.SeriesId);
            }
        }

        foreach (var source in sourceSeries)
        {
            AddSeries(source);
        }

        var categoryNames = categories.ToDictionary(category => category.Id, category => category.Name,
            StringComparer.Ordinal);

        string GetRegionId(string? subType)
        {
            return $"map-subtype-{GodlikeProjection.Get(subType).SubType}";
        }

        var anchorMarkers = FlattenAnchors(anchors)
            .Select(anchor =>
            {
                var subType = string.IsNullOrWhiteSpace(anchor.MapSubType) ? "1" : anchor.MapSubType;
                return new MapMarker
                {
                    Id = $"anchor:{anchor.Id}",
                    MapId = anchor.MapId,
                    RegionId = GetRegionId(subType),
                    MapSubType = subType,
                    CategoryId = "anchor",
                    CategoryIds = ["anchor"],
                    LayerId = anchor.Layer,
                    Title = anchor.Name,
                    Description = "区域锚点",
                    MapPosition = GodlikeProjection.WorldToMap(subType, anchor.X, anchor.Y),
                    WorldPosition = new MapPoint(anchor.X, anchor.Y)
                };
            })
            .ToList();
        if (anchorMarkers.Count > 0 && categoryIds.Add("anchor"))
        {
            categories.Add(new MapCategory
            {
                Id = "anchor",
                Name = "区域",
                Order = -1,
                IsVisibleByDefault = true
            });
        }

        var pointMarkers = points.Select(point =>
        {
            var ids = (point.Series ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var primary = ids.FirstOrDefault() ?? "unknown";
            if (primary == "unknown" && !categoryIds.Contains(primary))
            {
                categories.Add(new MapCategory
                {
                    Id = primary,
                    Name = "未分类",
                    Order = order++,
                    IsVisibleByDefault = true
                });
                categoryNames[primary] = "未分类";
            }

            var subType = string.IsNullOrWhiteSpace(point.MapSubType) ? "1" : point.MapSubType;
            return new MapMarker
            {
                Id = point.PointId,
                MapId = point.MapId,
                RegionId = GetRegionId(subType),
                MapSubType = subType,
                LayerId = point.Layer,
                CategoryId = primary,
                CategoryIds = ids,
                Title = point.Name,
                Description = point.MapSubType,
                Icon = point.Icon,
                MapPosition = GodlikeProjection.WorldToMap(subType, point.X, point.Y),
                WorldPosition = new MapPoint(point.X, point.Y),
                IsTeleport = ids.Any(id => categoryNames.TryGetValue(id, out var name) &&
                                           (name.Contains("传送", StringComparison.Ordinal) ||
                                            name.Contains("界碑", StringComparison.Ordinal)))
            };
        }).ToList();

        var allMarkers = pointMarkers.Concat(anchorMarkers).ToList();
        snapshot.Categories = categories.OrderBy(category => category.Order).ToList();
        snapshot.Regions = GodlikeProjection.Configs.Select(config =>
        {
            var regionId = GetRegionId(config.SubType);
            var layerIds = allMarkers
                .Where(marker => marker.RegionId == regionId)
                .Select(marker => marker.LayerId)
                .Where(layer => !string.IsNullOrWhiteSpace(layer))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(layer => layer)
                .ToList();
            return new MapRegion
            {
                Id = regionId,
                MapId = mapId,
                Name = config.Name,
                SubTypes = [config.SubType],
                MapZone = config.MapZone,
                TileGroup = config.TileGroup,
                TileExtent = config.TileExtent,
                TileUrlTemplate = config.TileUrlTemplate,
                TileMinZoom = 3,
                TileMaxZoom = 8,
                Layers = layerIds.Select((layer, index) => new MapLayer
                {
                    Id = layer!,
                    Name = FormatLayerName(layer!, index),
                    IconUrl = layer == "0"
                        ? "pack://application:,,,/BetterYYSLS;component/Assets/Map/layer-floor.png"
                        : "pack://application:,,,/BetterYYSLS;component/Assets/Map/layer-icon.png",
                    ActiveIconUrl = layer == "0"
                        ? "pack://application:,,,/BetterYYSLS;component/Assets/Map/layer-floor-active.png"
                        : "pack://application:,,,/BetterYYSLS;component/Assets/Map/layer-icon-active.png",
                    Floor = index,
                    IsVisibleByDefault = true
                }).ToList()
            };
        }).ToList();
        snapshot.Markers = allMarkers;
        return snapshot;
    }

    private static string FormatLayerName(string layer, int floor)
    {
        if (layer == "0")
        {
            return "地面";
        }

        var number = int.TryParse(layer, out var parsed) ? parsed : floor;
        var names = new[] { "零", "一", "二", "三", "四", "五", "六", "七", "八", "九" };
        return number >= 0 && number < names.Length ? $"{names[number]}层" : $"{layer}层";
    }

    private static IEnumerable<GodlikeAnchor> FlattenAnchors(IEnumerable<GodlikeAnchor> anchors)
    {
        foreach (var anchor in anchors)
        {
            yield return anchor;
            foreach (var child in FlattenAnchors(anchor.Children ?? []))
            {
                yield return child;
            }
        }
    }

    private static async Task<T> PostAsync<T>(
        GodlikeCredentials credentials,
        string path,
        object body,
        CancellationToken cancellationToken,
        string? baseUrl = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, (baseUrl ?? BaseUrl) + path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
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

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Godlike 接口 {path} 返回 {(int)response.StatusCode}。");
        }

        var result = JsonSerializer.Deserialize<GodlikeApiResponse<T>>(text, JsonOptions);
        if (result is null)
        {
            throw new InvalidOperationException($"Godlike 接口 {path} 响应为空。");
        }

        if (result.Code != 200 || result.Result is null)
        {
            throw new InvalidOperationException($"Godlike 接口 {path} 失败：{result.Errmsg}。");
        }

        return result.Result;
    }

    private sealed class GodlikeApiResponse<T>
    {
        public int Code { get; set; }
        public string? Errmsg { get; set; }
        public T? Result { get; set; }
    }

    private sealed class GodlikeMapInfoResult
    {
        public GodlikeMapInfo? Result { get; set; }
    }

    private sealed class GodlikeMapInfo
    {
        public string? MapId { get; set; }
    }

    private sealed class GodlikeClientMetaResult
    {
        public List<GodlikeClientMetaItem> Metas { get; set; } = [];
    }

    private sealed class GodlikeClientMetaItem
    {
        public string Key { get; set; } = string.Empty;
        public string? Value { get; set; }
    }

    private sealed class GodlikeClientOffsets
    {
        public GodlikeClientOffset? OffSet { get; set; }
        public string? CurVersion { get; set; }
    }

    private sealed class GodlikeClientOffset
    {
        public string? X { get; set; }
        public string? Y { get; set; }
        public string? Z { get; set; }
    }

    private sealed class GodlikePointDetail
    {
        public string PointId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Desc { get; set; }
        public string? CityName { get; set; }
        public string? Icon { get; set; }
        public string? DescIcon { get; set; }
        public string? LastSeriesName { get; set; }
        public string? MapSubType { get; set; }
        public bool Favorite { get; set; }
        public bool Visit { get; set; }
        public bool? RoleVisit { get; set; }
        public string? PointSourceType { get; set; }
        public string? RegionId { get; set; }
        public int TotalCommitterNum { get; set; }
        public List<GodlikeCommitter>? Committers { get; set; }
    }

    private sealed class GodlikeCommitter
    {
        public string? Name { get; set; }
    }

    private sealed class GodlikePointSeriesResult
    {
        public List<GodlikePointSeries> PointSeriesList { get; set; } = [];
    }

    private sealed class GodlikeAnchor
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public string MapId { get; set; } = string.Empty;
        public string? ParentId { get; set; }
        public string? Layer { get; set; }
        public string? MapSubType { get; set; }
        public List<GodlikeAnchor> Children { get; set; } = [];
    }

    private sealed class GodlikePointSeries
    {
        public string SeriesId { get; set; } = string.Empty;
        public string SeriesName { get; set; } = string.Empty;
        public bool DefaultShow { get; set; }
        public string? PosterUrl { get; set; }
        public List<GodlikePointSeries> Children { get; set; } = [];
    }

    private sealed class GodlikePoint
    {
        public string MapId { get; set; } = string.Empty;
        public string PointId { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public string? Icon { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<string> Series { get; set; } = [];
        public string? Layer { get; set; }
        public string? MapSubType { get; set; }
    }
}
