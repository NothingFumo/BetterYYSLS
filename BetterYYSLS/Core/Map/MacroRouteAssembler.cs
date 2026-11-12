using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace BetterYYSLS.Core.Map;

/// <summary>
/// 根据选中标记生成宏观访问序列。
/// 规划只使用水平距离和传送标记代价，不判断真实可走性。
/// 问题模型是多仓库开放路径（MDOVRP）：传送节点是仓库，路线从仓库出发且不返回，
/// 目标函数为步行距离 + 每次传送的固定代价。
/// 求解流程：点级聚类形成连走块 → 块内精确/启发式开放路径 → 块间 ALNS + 局部搜索。
/// </summary>
public static class MacroRouteAssembler
{
    /// <summary>
    /// 块内精确动态规划允许的点数上限；超过后使用最近邻 + 2-opt。
    /// </summary>
    private const int MaxExactOrderSize = 16;

    /// <summary>
    /// 全局精确开放路径允许的点数上限。
    /// </summary>
    private const int MaxExactGlobalSize = 18;

    /// <summary>
    /// 同一传送点附近的小范围点直接视为可连走，避免 r(p) 很小时被 max(r) 错误切开。
    /// 传送免费时使用更小的 150，让免费传送承担更多跨块移动。
    /// </summary>
    private const double LocalWalkMergeDistance = 200d;

    /// <summary>
    /// 连走合并允许的最大块规模；传送免费时使用更小的 24。
    /// </summary>
    private const int MaxMergedBlockSize = 40;

    /// <summary>
    /// 块间最近点超过该距离时优先传送，避免生成不可信的跨区步行长线。
    /// 仅在传送代价大于 0 时生效；免费传送完全由成本比较决定。
    /// 若传送代价超过步行的 MaxWalkTransitionCostRatio 倍，则允许长步行，尊重用户的成本设置。
    /// </summary>
    private const double MaxWalkTransitionDistance = 200d;

    /// <summary>
    /// 超过步行上限时，传送代价最多允许比步行高出的倍数；再高则允许长步行。
    /// </summary>
    private const double MaxWalkTransitionCostRatio = 1.5d;

    /// <summary>
    /// 小规模点级分区阈值；该规模下直接按最近传送点分组，避免整块被一个起点吸收。
    /// </summary>
    private const int SmallPartitionLimit = 32;

    /// <summary>
    /// 单次规划允许处理的目标点上限，避免超大集合把界面阻塞过久。
    /// 调用方仍应通过 maxPoints 主动限制；该值只是最后一道保护。
    /// </summary>
    private const int MaxPlanTargets = 2048;

    /// <summary>
    /// 2-opt 局部搜索允许的块数上限；更大的块序只用 relocate/swap。
    /// </summary>
    private const int MaxTwoOptBlocks = 64;

    /// <summary>
    /// 路线级拆分允许的最大 trip 点数。
    /// </summary>
    private const int MaxRouteSplitPoints = 2048;

    /// <summary>
    /// 路线级拆分允许的最大传送分组数。
    /// </summary>
    private const int MaxRouteSplitGroups = 64;

    /// <summary>
    /// 路线级拆分的总时间预算；预算内优先处理小 trip。
    /// </summary>
    private const int SplitTripBudgetMilliseconds = 300;

    /// <summary>
    /// 默认随机种子，保证相同输入得到相同路线。
    /// </summary>
    private const int DefaultSeed = 20261111;

    /// <summary>
    /// 规划一条宏观路线。
    /// </summary>
    /// <param name="snapshot">地图快照。</param>
    /// <param name="request">规划请求。</param>
    /// <returns>排序后的宏观路线。</returns>
    private static MacroRoute Plan(MapSnapshot snapshot, RouteRequest request)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxPoints < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "最大点数必须大于 0。");
        }

        if (request.WalkCostFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "步行代价系数必须大于 0。");
        }

        if (request.TeleportPenalty < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "传送代价不能为负数。");
        }

        // 先按请求顺序去重，并对超过上限的请求直接拒绝，避免 UI 产生不可控路线。
        var selectedIds = request.SelectedMarkerIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (selectedIds.Count > request.MaxPoints)
        {
            throw new ArgumentException($"选中点数不能超过 {request.MaxPoints}。", nameof(request));
        }

        var markerById = snapshot.Markers
            .GroupBy(marker => marker.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var selected = selectedIds
            .Where(markerById.ContainsKey)
            .Select(id => markerById[id])
            .ToList();

        var route = new MacroRoute();
        if (selected.Count == 0)
        {
            return route;
        }

        var start = request.Start ?? selected[0].WorldPosition;
        // 小规模用 Held-Karp 精确求解；其余用最近邻 + 2-opt/Or-opt。
        var order = request.UseExactOrder && selected.Count <= MaxExactOrderSize
            ? BuildExactOrder(start, selected, request)
            : selected.Count <= 256
                ? ImproveOrder(start, BuildNearestOrder(start, selected, request), request)
                : BuildNearestOrder(start, selected, request);

        route.OrderedMarkers = order;
        var current = start;
        foreach (var marker in order)
        {
            var distance = current.HorizontalDistanceTo(marker.WorldPosition);
            var cost = (distance * request.WalkCostFactor) + Penalty(marker, request);
            route.Legs.Add(new RouteLeg
            {
                From = current,
                To = marker.WorldPosition,
                Kind = marker.IsTeleport ? RouteLegKind.Teleport : RouteLegKind.EstimatedWalk,
                Distance = distance,
                Cost = cost
            });
            route.TotalCost += cost;
            route.WalkingDistance += distance;
            current = marker.WorldPosition;
        }

        return route;
    }

    /// <summary>
    /// 按传送节点聚类规划路线。
    /// 目标点始终以最近的界碑/传送点为锚点，路线从传送点开始；除首次外每次换传送支付固定代价。
    /// </summary>
    /// <param name="snapshot">地图快照。</param>
    /// <param name="selectedMarkerIds">选中的标记标识。</param>
    /// <param name="maxPoints">本次最多规划的点数。</param>
    /// <param name="start">可选起点；为空时从最近的传送点开始。</param>
    /// <param name="layerId">当前位面；用于优先选择同层传送点。</param>
    /// <param name="teleportCost">每次传送的固定代价。</param>
    /// <param name="sameLayerFallbackDistance">同层传送点超过该距离时允许跨层。</param>
    /// <param name="allowedTeleportIds">允许使用的传送点集合；为空表示不限制。</param>
    /// <param name="seed">随机种子，保证相同输入得到相同结果。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>宏观路线。</returns>
    public static MacroRoute PlanTeleportClusteredRoute(
        MapSnapshot snapshot,
        IEnumerable<string> selectedMarkerIds,
        int maxPoints = 128,
        MapPoint? start = null,
        string? layerId = null,
        double teleportCost = 0,
        double sameLayerFallbackDistance = 600d,
        IReadOnlyCollection<string>? allowedTeleportIds = null,
        int seed = DefaultSeed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(selectedMarkerIds);
        if (maxPoints < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPoints));
        }

        if (teleportCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(teleportCost), "传送代价不能为负数。");
        }

        if (sameLayerFallbackDistance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sameLayerFallbackDistance));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var selectedIdSet = selectedMarkerIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        var targets = snapshot.Markers
            .Where(marker => selectedIdSet.Contains(marker.Id))
            .Where(marker => !marker.IsTeleport)
            // 区域锚点不是玩法点，不能进入路线规划。
            .Where(marker => !string.Equals(marker.CategoryId, "anchor", StringComparison.Ordinal))
            .ToList();
        if (targets.Count == 0)
        {
            return new MacroRoute();
        }

        var regionIds = targets
            .Select(marker => marker.RegionId)
            .Where(regionId => !string.IsNullOrWhiteSpace(regionId))
            .ToHashSet(StringComparer.Ordinal);
        var allTeleports = snapshot.Markers
            .Where(marker => marker.IsTeleport)
            .Where(marker => regionIds.Count == 0 || regionIds.Contains(marker.RegionId))
            .Where(marker => allowedTeleportIds is null || allowedTeleportIds.Contains(marker.Id))
            .GroupBy(marker => marker.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        // 没有可用传送点时退化为普通开放路径。
        if (allTeleports.Count == 0)
        {
            var fallbackCount = Math.Min(Math.Min(maxPoints, MaxPlanTargets), targets.Count);
            return Plan(snapshot, new RouteRequest
            {
                Start = start,
                SelectedMarkerIds = targets.Take(fallbackCount).Select(marker => marker.Id).ToList(),
                MaxPoints = fallbackCount,
                TeleportPenalty = teleportCost
            });
        }

        var layerTeleports = allTeleports
            .Where(marker => string.IsNullOrWhiteSpace(layerId) || marker.LayerId == layerId)
            .ToList();

        // 目标点超过上限时按传送点分层抽样，避免只保留少数密集区域。
        var effectiveMax = Math.Min(maxPoints, MaxPlanTargets);
        if (targets.Count > effectiveMax)
        {
            targets = SelectTargetsForPlanning(
                targets,
                allTeleports,
                effectiveMax,
                cancellationToken);
        }

        var reference = start ?? targets[0].WorldPosition;
        // 同层传送点离起点过远时允许改用其他层，避免无显式起点时从远处落地。
        var preferredTeleports = layerTeleports;
        if (layerTeleports.Count > 0 &&
            layerTeleports.Min(teleport =>
                teleport.WorldPosition.HorizontalDistanceTo(reference)) >
            sameLayerFallbackDistance)
        {
            preferredTeleports = allTeleports;
        }

        var initialTeleport = TeleportSelector.SelectInitial(
            reference,
            preferredTeleports,
            allTeleports,
            sameLayerFallbackDistance);
        var startPoint = start ?? initialTeleport.WorldPosition;
        var hasExplicitStart = start.HasValue;

        var plannedPoints = new List<PlannedPoint>(targets.Count);
        foreach (var marker in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            plannedPoints.Add(CreatePlannedPoint(
                marker,
                layerTeleports,
                allTeleports,
                layerId,
                sameLayerFallbackDistance));
        }

        // 小规模直接按点级最近传送点分组，避免连走合并把不同传送点区域吞成一块。
        if (plannedPoints.Count <= SmallPartitionLimit)
        {
            return SplitTripsByTeleport(
                PlanSmallPartition(
                    plannedPoints,
                    startPoint,
                    hasExplicitStart,
                    teleportCost,
                    cancellationToken),
                layerTeleports,
                allTeleports,
                layerId,
                sameLayerFallbackDistance,
                teleportCost,
                cancellationToken);
        }

        // 传送免费时优先细粒度块，多利用免费传送降低步行；传送昂贵时合并更多点，减少传送次数。
        var freeTeleport = teleportCost <= 0d;
        var localMergeDistance = freeTeleport ? 150d : LocalWalkMergeDistance;
        var maxMergedBlockSize = freeTeleport ? 24 : MaxMergedBlockSize;
        var blocks = BuildWalkBlocks(
            plannedPoints,
            teleportCost,
            localMergeDistance,
            maxMergedBlockSize,
            cancellationToken);
        if (blocks.Count == 0)
        {
            return new MacroRoute();
        }

        // 允许块重新选择仓库地址，避免初始最近传送点聚类被永久固定。
        ImproveBlockTeleports(
            blocks,
            layerTeleports,
            allTeleports,
            layerId,
            sameLayerFallbackDistance,
            cancellationToken);

        // 块内路线只计算一次，块序搜索使用固定出口和块间边权，避免重复指数 DP。
        PrepareTeleportRoutes(blocks, cancellationToken);

        var model = BuildOrderModel(blocks, startPoint, hasExplicitStart, teleportCost);
        var initialOrder = BuildInitialOrder(model);
        var orderedBlocks = OptimizeBlockOrder(
            model,
            initialOrder,
            new Random(seed),
            cancellationToken);

        return SplitTripsByTeleport(
            BuildRoute(
                blocks,
                orderedBlocks,
                startPoint,
                hasExplicitStart,
                teleportCost,
                cancellationToken),
            layerTeleports,
            allTeleports,
            layerId,
            sameLayerFallbackDistance,
            teleportCost,
            cancellationToken);
    }

    /// <summary>
    /// 规划一条精确的带传送开放路径。
    /// </summary>
    private static MacroRoute PlanBlockWithTeleports(
        IReadOnlyList<MapMarker> markers,
        IReadOnlyList<MapMarker> teleports,
        double teleportCost,
        CancellationToken cancellationToken)
    {
        var count = markers.Count;
        if (count == 0)
        {
            return new MacroRoute();
        }

        var stateCount = 1 << count;
        var size = stateCount * count;
        var costs = ArrayPool<double>.Shared.Rent(size);
        var previous = ArrayPool<sbyte>.Shared.Rent(size);
        var teleported = ArrayPool<bool>.Shared.Rent(size);
        var distances = ArrayPool<double>.Shared.Rent(count * count);
        var startCosts = ArrayPool<double>.Shared.Rent(count);
        var teleportStepCosts = ArrayPool<double>.Shared.Rent(count);
        try
        {
            Array.Fill(costs, double.PositiveInfinity, 0, size);
            Array.Fill(previous, (sbyte)-1, 0, size);
            Array.Fill(teleported, false, 0, size);

            for (var left = 0; left < count; left++)
            {
                for (var right = 0; right < count; right++)
                {
                    distances[(left * count) + right] = markers[left].WorldPosition
                        .HorizontalDistanceTo(markers[right].WorldPosition);
                }
            }

            // σ 出发边：w(σ,p)=min_t d(t,p)。
            for (var index = 0; index < count; index++)
            {
                startCosts[index] = NearestTeleportDistance(
                    markers[index].WorldPosition,
                    teleports);
                teleportStepCosts[index] = startCosts[index];
                costs[((1 << index) * count) + index] = startCosts[index];
            }

            var fullState = stateCount - 1;
            for (var state = 1; state < fullState; state++)
            {
                if ((state & 0xFFF) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var remaining = fullState ^ state;
                var lastBits = state;
                while (lastBits != 0)
                {
                    var last = BitOperations.TrailingZeroCount(lastBits);
                    lastBits &= lastBits - 1;
                    var current = costs[(state * count) + last];
                    if (double.IsPositiveInfinity(current))
                    {
                        continue;
                    }

                    var nextBits = remaining;
                    while (nextBits != 0)
                    {
                        var next = BitOperations.TrailingZeroCount(nextBits);
                        nextBits &= nextBits - 1;
                        var nextState = state | (1 << next);
                        var index = (nextState * count) + next;
                        var walkCost = current + distances[(last * count) + next];
                        if (walkCost < costs[index])
                        {
                            costs[index] = walkCost;
                            previous[index] = (sbyte)last;
                            teleported[index] = false;
                        }

                        var teleportCostForNext = current +
                            teleportCost +
                            teleportStepCosts[next];
                        if (teleportCostForNext < costs[index])
                        {
                            costs[index] = teleportCostForNext;
                            previous[index] = (sbyte)last;
                            teleported[index] = true;
                        }
                    }
                }
            }

            var bestLast = 0;
            for (var index = 1; index < count; index++)
            {
                if (costs[(fullState * count) + index] <
                    costs[(fullState * count) + bestLast])
                {
                    bestLast = index;
                }
            }

            if (double.IsPositiveInfinity(costs[(fullState * count) + bestLast]))
            {
                throw new InvalidOperationException("精确路径找不到可行解，请检查点位坐标。");
            }

            var order = new List<int>(count);
            var flags = new List<bool>(count);
            var stateAt = fullState;
            var currentIndex = bestLast;
            while (currentIndex >= 0)
            {
                order.Add(currentIndex);
                flags.Add(teleported[(stateAt * count) + currentIndex]);
                var prior = previous[(stateAt * count) + currentIndex];
                stateAt &= ~(1 << currentIndex);
                currentIndex = prior;
            }

            order.Reverse();
            flags.Reverse();

            var route = new MacroRoute { ClusterCount = 1 };
            var initialMarker = markers[order[0]];
            var currentPoint = NearestTeleport(initialMarker.WorldPosition, teleports).WorldPosition;
            var arrivedByTeleport = true;
            for (var index = 0; index < order.Count; index++)
            {
                var marker = markers[order[index]];
                if (index > 0 && flags[index])
                {
                    var chosenTeleport = NearestTeleport(marker.WorldPosition, teleports);
                    AddTeleportLeg(route, currentPoint, chosenTeleport, 0, teleportCost);
                    currentPoint = chosenTeleport.WorldPosition;
                    arrivedByTeleport = true;
                }

                var distance = currentPoint.HorizontalDistanceTo(marker.WorldPosition);
                route.Legs.Add(new RouteLeg
                {
                    From = currentPoint,
                    To = marker.WorldPosition,
                    Kind = arrivedByTeleport ? RouteLegKind.TeleportArrival : RouteLegKind.EstimatedWalk,
                    Distance = distance,
                    Cost = distance
                });
                route.WalkingDistance += distance;
                route.TotalCost += distance;
                route.OrderedMarkers.Add(marker);
                currentPoint = marker.WorldPosition;
                arrivedByTeleport = false;
            }

            return route;
        }
        finally
        {
            ArrayPool<double>.Shared.Return(costs);
            ArrayPool<sbyte>.Shared.Return(previous);
            ArrayPool<bool>.Shared.Return(teleported);
            ArrayPool<double>.Shared.Return(distances);
            ArrayPool<double>.Shared.Return(startCosts);
            ArrayPool<double>.Shared.Return(teleportStepCosts);
        }
    }

    /// <summary>
    /// 点级按最近传送点分组，每组形成一条独立路线。
    /// 这样小规模路线不会被起点附近的传送点整块吸收。
    /// </summary>
    private static MacroRoute PlanSmallPartition(
        IReadOnlyList<PlannedPoint> points,
        MapPoint startPoint,
        bool hasExplicitStart,
        double teleportCost,
        CancellationToken cancellationToken)
    {
        var groups = points
            .GroupBy(point => point.Teleport.Id, StringComparer.Ordinal)
            .Select(group => group.ToList())
            .OrderBy(group => group.Min(point =>
                startPoint.HorizontalDistanceTo(point.Marker.WorldPosition)))
            .ToList();

        var blocks = new List<WalkBlock>(groups.Count);
        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var block = new WalkBlock(group, group[0].Teleport);
            block.TeleportRoute = BuildPointRoute(
                block,
                block.Teleport.WorldPosition,
                cancellationToken);
            blocks.Add(block);
        }

        return BuildRoute(
            blocks,
            Enumerable.Range(0, blocks.Count).ToList(),
            startPoint,
            hasExplicitStart,
            teleportCost,
            cancellationToken);
    }

    /// <summary>
    /// 连走图中的一个点及其最近传送点。
    /// </summary>
    private sealed record PlannedPoint(
        MapMarker Marker,
        MapMarker Teleport,
        double TeleportDistance);

    /// <summary>
    /// 一个连走块的块内路线。
    /// </summary>
    private sealed class PointRoute
    {
        /// <summary>
        /// 创建块内路线。
        /// </summary>
        public PointRoute(MapPoint start, int[] order, double cost, MapPoint exit)
        {
            Start = start;
            Order = order;
            Cost = cost;
            Exit = exit;
        }

        /// <summary>
        /// 块内路线的起点。
        /// </summary>
        public MapPoint Start { get; }

        /// <summary>
        /// 访问顺序，元素是块内点索引。
        /// </summary>
        public int[] Order { get; }

        /// <summary>
        /// 从起点进入块并走完所有点的总代价。
        /// </summary>
        public double Cost { get; }

        /// <summary>
        /// 块内路线终点。
        /// </summary>
        public MapPoint Exit { get; }
    }

    /// <summary>
    /// 必须连走的一组点。
    /// </summary>
    private sealed class WalkBlock
    {
        /// <summary>
        /// 创建连走块。
        /// </summary>
        public WalkBlock(IReadOnlyList<PlannedPoint> points, MapMarker teleport)
        {
            Points = points.ToList();
            Teleport = teleport;
            UpdateTeleportEntry();
        }

        /// <summary>
        /// 块内点集合。
        /// </summary>
        public List<PlannedPoint> Points { get; }

        /// <summary>
        /// 块当前选用的仓库/传送点。
        /// </summary>
        public MapMarker Teleport { get; private set; }

        /// <summary>
        /// 从当前传送点步行到块内最近点的距离。
        /// </summary>
        public double TeleportEntryDistance { get; private set; }

        /// <summary>
        /// 从当前传送点出发的块内路线。
        /// </summary>
        public PointRoute? TeleportRoute { get; set; }

        /// <summary>
        /// 块间代价估计使用的固定出口。
        /// </summary>
        public MapPoint Exit => TeleportRoute?.Exit ??
            (Points.Count > 0 ? Points[0].Marker.WorldPosition : default);

        /// <summary>
        /// 切换块的仓库地址，并重新计算入口距离。
        /// </summary>
        public void SetTeleport(MapMarker teleport)
        {
            Teleport = teleport;
            UpdateTeleportEntry();
            TeleportRoute = null;
        }

        /// <summary>
        /// 重新计算传送点到块内最近点的距离。
        /// </summary>
        private void UpdateTeleportEntry()
        {
            TeleportEntryDistance = Points.Count == 0
                ? 0d
                : Points.Min(point =>
                    Teleport.WorldPosition.HorizontalDistanceTo(point.Marker.WorldPosition));
        }
    }

    /// <summary>
    /// 块序搜索使用的固定边权模型。
    /// 边权只依赖前一块出口和后一块入口，因此插入、交换和重定位都是 O(1) 增量。
    /// </summary>
    private sealed class BlockOrderModel
    {
        /// <summary>
        /// 创建块序模型。
        /// </summary>
        public BlockOrderModel(WalkBlock[] blocks, double[,] edges, double[] startEdges)
        {
            Blocks = blocks;
            Edges = edges;
            StartEdges = startEdges;
        }

        /// <summary>
        /// 全部连走块。
        /// </summary>
        public WalkBlock[] Blocks { get; }

        /// <summary>
        /// 块间转移代价。
        /// </summary>
        public double[,] Edges { get; }

        /// <summary>
        /// 从起点进入第一个块的代价。
        /// </summary>
        public double[] StartEdges { get; }

        /// <summary>
        /// 块数量。
        /// </summary>
        public int Count => Blocks.Length;

        /// <summary>
        /// 读取转移代价；from 为 -1 表示起点，to 为 -1 表示路线结束。
        /// </summary>
        public double Edge(int from, int to)
        {
            if (to < 0)
            {
                return 0d;
            }

            return from < 0 ? StartEdges[to] : Edges[from, to];
        }
    }

    /// <summary>
    /// 为每个点选择最近传送点；同层传送点足够近时优先同层。
    /// </summary>
    private static PlannedPoint CreatePlannedPoint(
        MapMarker marker,
        IReadOnlyList<MapMarker> layerTeleports,
        IReadOnlyList<MapMarker> allTeleports,
        string? layerId,
        double fallbackDistance)
    {
        var teleport = TeleportSelector.SelectAccess(
            marker,
            layerTeleports,
            allTeleports,
            layerId,
            fallbackDistance);

        return new PlannedPoint(
            marker,
            teleport,
            marker.WorldPosition.HorizontalDistanceTo(teleport.WorldPosition));
    }

    /// <summary>
    /// 找到离指定坐标最近的传送点。
    /// </summary>
    private static MapMarker NearestTeleport(
        MapPoint point,
        IReadOnlyList<MapMarker> teleports)
    {
        var best = teleports[0];
        var bestSquared = double.PositiveInfinity;
        foreach (var teleport in teleports)
        {
            var dx = point.X - teleport.WorldPosition.X;
            var dy = point.Y - teleport.WorldPosition.Y;
            var squared = (dx * dx) + (dy * dy);
            if (squared < bestSquared)
            {
                bestSquared = squared;
                best = teleport;
            }
        }

        return best;
    }

    /// <summary>
    /// 目标点超过上限时按最近传送点分组，轮转选取离传送点最近的点。
    /// 这样每个传送簇都能进入规划，不会因为整体排序丢掉整个区域。
    /// </summary>
    private static List<MapMarker> SelectTargetsForPlanning(
        IReadOnlyList<MapMarker> targets,
        IReadOnlyList<MapMarker> teleports,
        int maxCount,
        CancellationToken cancellationToken)
    {
        var groups = targets
            .Select(marker => (
                marker,
                teleport: NearestTeleport(marker.WorldPosition, teleports)))
            .GroupBy(item => item.teleport.Id, StringComparer.Ordinal)
            .Select(group => new Queue<MapMarker>(
                group
                    .OrderBy(item => item.marker.WorldPosition
                        .HorizontalDistanceTo(item.teleport.WorldPosition))
                    .Select(item => item.marker)))
            .OrderByDescending(queue => queue.Count)
            .ToList();

        var selected = new List<MapMarker>(maxCount);
        while (selected.Count < maxCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var added = false;
            foreach (var queue in groups)
            {
                if (selected.Count >= maxCount)
                {
                    break;
                }

                if (queue.Count == 0)
                {
                    continue;
                }

                selected.Add(queue.Dequeue());
                added = true;
            }

            if (!added)
            {
                break;
            }
        }

        return selected;
    }

    /// <summary>
    /// 返回离指定坐标最近的传送点距离。
    /// </summary>
    private static double NearestTeleportDistance(
        MapPoint point,
        IReadOnlyList<MapMarker> teleports)
    {
        var bestSquared = double.PositiveInfinity;
        foreach (var teleport in teleports)
        {
            var dx = point.X - teleport.WorldPosition.X;
            var dy = point.Y - teleport.WorldPosition.Y;
            var squared = (dx * dx) + (dy * dy);
            if (squared < bestSquared)
            {
                bestSquared = squared;
            }
        }

        return Math.Sqrt(bestSquared);
    }

    /// <summary>
    /// Phase 2：以最近邻边和近距离边做降序节约量合并。
    /// </summary>
    private static List<WalkBlock> BuildWalkBlocks(
        IReadOnlyList<PlannedPoint> points,
        double teleportCost,
        double localMergeDistance,
        int maxMergedBlockSize,
        CancellationToken cancellationToken)
    {
        var count = points.Count;
        var parent = Enumerable.Range(0, count).ToArray();
        var componentSize = Enumerable.Repeat(1, count).ToArray();

        int Find(int value)
        {
            while (parent[value] != value)
            {
                parent[value] = parent[parent[value]];
                value = parent[value];
            }

            return value;
        }

        void Union(int left, int right)
        {
            var rootLeft = Find(left);
            var rootRight = Find(right);
            if (rootLeft == rootRight ||
                componentSize[rootLeft] + componentSize[rootRight] > maxMergedBlockSize)
            {
                return;
            }

            parent[rootRight] = rootLeft;
            componentSize[rootLeft] += componentSize[rootRight];
        }

        var edges = new List<(int Left, int Right, double Saving)>(count * 2);
        // 每个点只连最近邻，避免在传送点边界产生大量无意义候选边。
        for (var left = 0; left < count; left++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nearestIndex = -1;
            var nearestSquared = double.PositiveInfinity;
            for (var right = 0; right < count; right++)
            {
                if (left == right)
                {
                    continue;
                }

                var dx = points[left].Marker.WorldPosition.X - points[right].Marker.WorldPosition.X;
                var dy = points[left].Marker.WorldPosition.Y - points[right].Marker.WorldPosition.Y;
                var squared = (dx * dx) + (dy * dy);
                if (squared < nearestSquared)
                {
                    nearestSquared = squared;
                    nearestIndex = right;
                }
            }

            if (nearestIndex < 0)
            {
                continue;
            }

            var distance = Math.Sqrt(nearestSquared);
            if (distance >= Math.Max(
                    points[left].TeleportDistance,
                    points[nearestIndex].TeleportDistance))
            {
                continue;
            }

            edges.Add((
                left,
                nearestIndex,
                points[left].TeleportDistance + points[nearestIndex].TeleportDistance - distance));
        }

        // 近距离点无条件作为候选；否则传送点附近 r(p) 很小时会被 max(r) 漏合并。
        var localThresholdSquared = localMergeDistance * localMergeDistance;
        for (var left = 0; left < count; left++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var right = left + 1; right < count; right++)
            {
                var dx = points[left].Marker.WorldPosition.X - points[right].Marker.WorldPosition.X;
                var dy = points[left].Marker.WorldPosition.Y - points[right].Marker.WorldPosition.Y;
                var squared = (dx * dx) + (dy * dy);
                if (squared > localThresholdSquared)
                {
                    continue;
                }

                var distance = Math.Sqrt(squared);
                edges.Add((
                    left,
                    right,
                    points[left].TeleportDistance + points[right].TeleportDistance - distance));
            }
        }

        edges.Sort((left, right) => right.Saving.CompareTo(left.Saving));
        foreach (var (left, right, _) in edges)
        {
            Union(left, right);
        }

        var components = points
            .Select((point, index) => (point, root: Find(index)))
            .GroupBy(item => item.root)
            .Select(group => group.Select(item => item.point).ToList())
            .ToList();

        var blocks = new List<WalkBlock>(components.Count);
        foreach (var component in components)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var teleport = component
                .OrderBy(point => point.TeleportDistance)
                .First().Teleport;
            blocks.Add(new WalkBlock(component, teleport));
        }

        return SplitMixedBlocks(blocks, teleportCost);
    }

    /// <summary>
    /// 混合块如果按最近传送点拆开后总成本更低，则拆成多个块。
    /// 这里用最近邻路径近似比较，避免在拆分决策阶段反复做指数 DP。
    /// </summary>
    private static List<WalkBlock> SplitMixedBlocks(
        IReadOnlyList<WalkBlock> blocks,
        double teleportCost)
    {
        var result = new List<WalkBlock>(blocks.Count);
        foreach (var block in blocks)
        {
            var groups = block.Points
                .GroupBy(point => point.Teleport.Id, StringComparer.Ordinal)
                .Select(group => group.ToList())
                .ToList();
            if (groups.Count <= 1)
            {
                result.Add(block);
                continue;
            }

            var singleCost = ApproximatePathCost(
                block.Points,
                block.Teleport.WorldPosition);
            var splitCost = 0d;
            var splitBlocks = new List<WalkBlock>(groups.Count);
            foreach (var group in groups)
            {
                var teleport = group[0].Teleport;
                splitBlocks.Add(new WalkBlock(group, teleport));
                splitCost += ApproximatePathCost(group, teleport.WorldPosition);
            }

            splitCost += teleportCost * (groups.Count - 1);
            if (splitCost + 1e-9 < singleCost)
            {
                result.AddRange(splitBlocks);
            }
            else
            {
                result.Add(block);
            }
        }

        return result;
    }

    /// <summary>
    /// 允许每个块重新选择最合适的传送点，避免初始最近点聚类被永久固定。
    /// 同层传送点足够近时仍优先同层，保持位面约束。
    /// </summary>
    private static void ImproveBlockTeleports(
        IReadOnlyList<WalkBlock> blocks,
        IReadOnlyList<MapMarker> layerTeleports,
        IReadOnlyList<MapMarker> allTeleports,
        string? layerId,
        double fallbackDistance,
        CancellationToken cancellationToken)
    {
        if (allTeleports.Count <= 1)
        {
            return;
        }

        foreach (var block in blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = SelectCandidateTeleports(
                block,
                layerTeleports,
                allTeleports,
                layerId,
                fallbackDistance);
            if (candidates.Count <= 1)
            {
                continue;
            }

            var best = block.Teleport;
            var bestCost = ApproximatePathCost(block.Points, best.WorldPosition);
            foreach (var candidate in candidates)
            {
                if (ReferenceEquals(candidate, block.Teleport))
                {
                    continue;
                }

                var cost = ApproximatePathCost(block.Points, candidate.WorldPosition);
                if (cost + 1e-9 < bestCost)
                {
                    bestCost = cost;
                    best = candidate;
                }
            }

            if (!ReferenceEquals(best, block.Teleport))
            {
                block.SetTeleport(best);
            }
        }
    }

    /// <summary>
    /// 为块挑选候选传送点：同层可用时只在该层内选择；大地图只取最近若干个。
    /// </summary>
    private static List<MapMarker> SelectCandidateTeleports(
        WalkBlock block,
        IReadOnlyList<MapMarker> layerTeleports,
        IReadOnlyList<MapMarker> allTeleports,
        string? layerId,
        double fallbackDistance)
    {
        return TeleportSelector.SelectCandidates(
            block.Points.Select(point => point.Marker).ToList(),
            layerTeleports,
            allTeleports,
            layerId,
            fallbackDistance);
    }

    /// <summary>
    /// 为每个块计算一次从传送点出发的块内路线。
    /// 超过时间预算后改用启发式，避免大量块触发指数 DP。
    /// </summary>
    private static void PrepareTeleportRoutes(
        IReadOnlyList<WalkBlock> blocks,
        CancellationToken cancellationToken)
    {
        var budget = Stopwatch.StartNew();
        foreach (var block in blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            block.TeleportRoute = BuildPointRoute(
                block,
                block.Teleport.WorldPosition,
                cancellationToken,
                forceHeuristic: budget.ElapsedMilliseconds > 300);
        }
    }

    /// <summary>
    /// 计算块内开放路径；小规模精确，大规模最近邻 + 2-opt。
    /// </summary>
    private static PointRoute BuildPointRoute(
        WalkBlock block,
        MapPoint start,
        CancellationToken cancellationToken,
        bool forceHeuristic = false)
    {
        var points = block.Points;
        var count = points.Count;
        if (count == 0)
        {
            return new PointRoute(start, [], 0d, start);
        }

        return !forceHeuristic && count <= MaxExactOrderSize
            ? SolveExactPointRoute(points, start, cancellationToken)
            : SolveHeuristicPointRoute(points, start, cancellationToken);
    }

    /// <summary>
    /// Held-Karp 求固定起点的块内最短开放路径。
    /// </summary>
    private static PointRoute SolveExactPointRoute(
        IReadOnlyList<PlannedPoint> points,
        MapPoint start,
        CancellationToken cancellationToken)
    {
        var count = points.Count;
        var stateCount = 1 << count;
        var size = stateCount * count;
        var costs = ArrayPool<double>.Shared.Rent(size);
        var previous = ArrayPool<sbyte>.Shared.Rent(size);
        var distances = ArrayPool<double>.Shared.Rent(count * count);
        try
        {
            Array.Fill(costs, double.PositiveInfinity, 0, size);
            Array.Fill(previous, (sbyte)-1, 0, size);
            for (var left = 0; left < count; left++)
            {
                for (var right = 0; right < count; right++)
                {
                    distances[(left * count) + right] = points[left].Marker.WorldPosition
                        .HorizontalDistanceTo(points[right].Marker.WorldPosition);
                }
            }

            for (var index = 0; index < count; index++)
            {
                costs[((1 << index) * count) + index] = start
                    .HorizontalDistanceTo(points[index].Marker.WorldPosition);
            }

            var fullState = stateCount - 1;
            for (var state = 1; state < fullState; state++)
            {
                if ((state & 0xFFF) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var remaining = fullState ^ state;
                var lastBits = state;
                while (lastBits != 0)
                {
                    var last = BitOperations.TrailingZeroCount(lastBits);
                    lastBits &= lastBits - 1;
                    var current = costs[(state * count) + last];
                    if (double.IsPositiveInfinity(current))
                    {
                        continue;
                    }

                    var nextBits = remaining;
                    while (nextBits != 0)
                    {
                        var next = BitOperations.TrailingZeroCount(nextBits);
                        nextBits &= nextBits - 1;
                        var nextState = state | (1 << next);
                        var index = (nextState * count) + next;
                        var cost = current + distances[(last * count) + next];
                        if (cost < costs[index])
                        {
                            costs[index] = cost;
                            previous[index] = (sbyte)last;
                        }
                    }
                }
            }

            var bestLast = 0;
            for (var index = 1; index < count; index++)
            {
                if (costs[(fullState * count) + index] <
                    costs[(fullState * count) + bestLast])
                {
                    bestLast = index;
                }
            }

            if (double.IsPositiveInfinity(costs[(fullState * count) + bestLast]))
            {
                return SolveHeuristicPointRoute(points, start, cancellationToken);
            }

            var order = new List<int>(count);
            var stateAt = fullState;
            var currentIndex = bestLast;
            while (currentIndex >= 0)
            {
                order.Add(currentIndex);
                var prior = previous[(stateAt * count) + currentIndex];
                stateAt &= ~(1 << currentIndex);
                currentIndex = prior;
            }

            order.Reverse();
            var exit = points[order[^1]].Marker.WorldPosition;
            return new PointRoute(
                start,
                order.ToArray(),
                costs[(fullState * count) + bestLast],
                exit);
        }
        finally
        {
            ArrayPool<double>.Shared.Return(costs);
            ArrayPool<sbyte>.Shared.Return(previous);
            ArrayPool<double>.Shared.Return(distances);
        }
    }

    /// <summary>
    /// 最近邻构造块内顺序，再用 2-opt 改进。
    /// </summary>
    private static PointRoute SolveHeuristicPointRoute(
        IReadOnlyList<PlannedPoint> points,
        MapPoint start,
        CancellationToken cancellationToken)
    {
        var count = points.Count;
        var remaining = Enumerable.Range(0, count).ToList();
        var order = new List<int>(count);
        var current = start;
        while (remaining.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bestIndex = 0;
            var bestCost = double.PositiveInfinity;
            for (var index = 0; index < remaining.Count; index++)
            {
                var cost = current.HorizontalDistanceTo(
                    points[remaining[index]].Marker.WorldPosition);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestIndex = index;
                }
            }

            var next = remaining[bestIndex];
            order.Add(next);
            remaining.RemoveAt(bestIndex);
            current = points[next].Marker.WorldPosition;
        }

        var bestCostValue = PathCost(points, order, start);
        for (var pass = 0; pass < 3; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var improved = false;
            for (var from = 0; from < order.Count - 1; from++)
            {
                for (var to = from + 1; to < order.Count; to++)
                {
                    order.Reverse(from, to - from + 1);
                    var cost = PathCost(points, order, start);
                    if (cost + 1e-9 < bestCostValue)
                    {
                        bestCostValue = cost;
                        improved = true;
                    }
                    else
                    {
                        order.Reverse(from, to - from + 1);
                    }
                }
            }

            if (!improved)
            {
                break;
            }
        }

        return new PointRoute(
            start,
            order.ToArray(),
            bestCostValue,
            points[order[^1]].Marker.WorldPosition);
    }

    /// <summary>
    /// 计算从起点按给定顺序访问所有点的总距离。
    /// </summary>
    private static double PathCost(
        IReadOnlyList<PlannedPoint> points,
        IReadOnlyList<int> order,
        MapPoint start)
    {
        var current = start;
        var cost = 0d;
        foreach (var index in order)
        {
            cost += current.HorizontalDistanceTo(points[index].Marker.WorldPosition);
            current = points[index].Marker.WorldPosition;
        }

        return cost;
    }

    /// <summary>
    /// 用最近邻估算从指定传送点进入块并走完全部点的代价。
    /// 只用于块拆分和仓库重分配决策。
    /// </summary>
    private static double ApproximatePathCost(
        IReadOnlyList<PlannedPoint> points,
        MapPoint start)
    {
        if (points.Count == 0)
        {
            return 0d;
        }

        var remaining = Enumerable.Range(0, points.Count).ToList();
        var current = start;
        var cost = 0d;
        while (remaining.Count > 0)
        {
            var bestIndex = 0;
            var bestCost = double.PositiveInfinity;
            for (var index = 0; index < remaining.Count; index++)
            {
                var distance = current.HorizontalDistanceTo(
                    points[remaining[index]].Marker.WorldPosition);
                if (distance < bestCost)
                {
                    bestCost = distance;
                    bestIndex = index;
                }
            }

            var next = remaining[bestIndex];
            remaining.RemoveAt(bestIndex);
            cost += bestCost;
            current = points[next].Marker.WorldPosition;
        }

        return cost;
    }

    /// <summary>
    /// 构建块序模型；边权包含步行和传送两种进入方式中更便宜的一种。
    /// 付费传送时超过步行上限且传送费用可接受的边直接按传送计费，与最终逐块决策保持一致。
    /// </summary>
    private static BlockOrderModel BuildOrderModel(
        IReadOnlyList<WalkBlock> blocks,
        MapPoint start,
        bool hasExplicitStart,
        double teleportCost)
    {
        var count = blocks.Count;
        var maxWalkTransitionDistance = teleportCost <= 0d
            ? double.PositiveInfinity
            : MaxWalkTransitionDistance;
        var edges = new double[count, count];
        for (var left = 0; left < count; left++)
        {
            for (var right = 0; right < count; right++)
            {
                if (left == right)
                {
                    continue;
                }

                var walk = NearestPointDistance(
                    blocks[left].Exit,
                    blocks[right].Points);
                var teleport = teleportCost + blocks[right].TeleportEntryDistance;
                // 付费传送时超过步行上限的边必须按传送计费，和最终逐块决策保持一致。
                var effectiveWalk = walk > maxWalkTransitionDistance &&
                                    teleport <= walk * MaxWalkTransitionCostRatio
                    ? double.PositiveInfinity
                    : walk;
                edges[left, right] = Math.Min(effectiveWalk, teleport);
            }
        }

        var startEdges = new double[count];
        for (var index = 0; index < count; index++)
        {
            var walk = NearestPointDistance(start, blocks[index].Points);
            // 首次进入可以视为从玩家位置直接走，或从传送点落地；有显式起点时传送计一次代价。
            var teleport = (hasExplicitStart ? teleportCost : 0d) +
                           blocks[index].TeleportEntryDistance;
            startEdges[index] = Math.Min(walk, teleport);
        }

        return new BlockOrderModel(blocks.ToArray(), edges, startEdges);
    }

    /// <summary>
    /// 返回点到块内最近点的距离。
    /// </summary>
    private static double NearestPointDistance(
        MapPoint point,
        IReadOnlyList<PlannedPoint> points)
    {
        var bestSquared = double.PositiveInfinity;
        foreach (var candidate in points)
        {
            var dx = point.X - candidate.Marker.WorldPosition.X;
            var dy = point.Y - candidate.Marker.WorldPosition.Y;
            var squared = (dx * dx) + (dy * dy);
            if (squared < bestSquared)
            {
                bestSquared = squared;
            }
        }

        return Math.Sqrt(bestSquared);
    }

    /// <summary>
    /// 生成三个初始解，取代理代价更小的一个：
    /// 最近邻、双层 Voronoi、贪心插入。
    /// </summary>
    private static List<int> BuildInitialOrder(BlockOrderModel model)
    {
        var best = BuildNearestBlockOrder(model);
        var bestCost = OrderCost(best, model);
        var voronoi = BuildVoronoiBlockOrder(model);
        var voronoiCost = OrderCost(voronoi, model);
        if (voronoiCost < bestCost)
        {
            best = voronoi;
            bestCost = voronoiCost;
        }

        // 贪心插入是 O(B^3)，只在中型实例上作为额外初始解。
        if (model.Count <= 160)
        {
            var greedy = BuildGreedyInsertionOrder(model);
            if (OrderCost(greedy, model) < bestCost)
            {
                best = greedy;
            }
        }

        return best;
    }

    /// <summary>
    /// 贪心插入初始解：从离起点最近的块开始，每次把边际代价最小的块插入最佳位置。
    /// </summary>
    private static List<int> BuildGreedyInsertionOrder(BlockOrderModel model)
    {
        if (model.Count == 0)
        {
            return [];
        }

        var remaining = Enumerable.Range(0, model.Count).ToList();
        var seedPosition = 0;
        var seedCost = double.PositiveInfinity;
        for (var index = 0; index < remaining.Count; index++)
        {
            var cost = model.StartEdges[remaining[index]];
            if (cost < seedCost)
            {
                seedCost = cost;
                seedPosition = index;
            }
        }

        var order = new List<int>(model.Count) { remaining[seedPosition] };
        remaining.RemoveAt(seedPosition);
        while (remaining.Count > 0)
        {
            var bestBlockPosition = 0;
            var bestInsertPosition = 0;
            var bestDelta = double.PositiveInfinity;
            for (var blockPosition = 0; blockPosition < remaining.Count; blockPosition++)
            {
                var block = remaining[blockPosition];
                for (var insertPosition = 0; insertPosition <= order.Count; insertPosition++)
                {
                    var delta = InsertionDelta(order, block, insertPosition, model);
                    if (delta < bestDelta)
                    {
                        bestDelta = delta;
                        bestBlockPosition = blockPosition;
                        bestInsertPosition = insertPosition;
                    }
                }
            }

            order.Insert(bestInsertPosition, remaining[bestBlockPosition]);
            remaining.RemoveAt(bestBlockPosition);
        }

        return order;
    }

    /// <summary>
    /// 从当前点开始逐块选择最近的块。
    /// </summary>
    private static List<int> BuildNearestBlockOrder(BlockOrderModel model)
    {
        var remaining = Enumerable.Range(0, model.Count).ToList();
        var order = new List<int>(model.Count);
        var previous = -1;
        while (remaining.Count > 0)
        {
            var bestPosition = 0;
            var bestCost = double.PositiveInfinity;
            for (var index = 0; index < remaining.Count; index++)
            {
                var cost = model.Edge(previous, remaining[index]);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestPosition = index;
                }
            }

            var next = remaining[bestPosition];
            order.Add(next);
            remaining.RemoveAt(bestPosition);
            previous = next;
        }

        return order;
    }

    /// <summary>
    /// 双层 Voronoi 初始解：上层按最近传送点聚类，下层在簇内按最近块排序。
    /// </summary>
    private static List<int> BuildVoronoiBlockOrder(BlockOrderModel model)
    {
        var groups = Enumerable.Range(0, model.Count)
            .GroupBy(index => model.Blocks[index].Teleport.Id, StringComparer.Ordinal)
            .Select(group => group.ToList())
            .OrderBy(group => group.Min(index => model.StartEdges[index]))
            .ToList();

        var order = new List<int>(model.Count);
        var previous = -1;
        foreach (var group in groups)
        {
            var remaining = group.ToList();
            while (remaining.Count > 0)
            {
                var bestPosition = 0;
                var bestCost = double.PositiveInfinity;
                for (var index = 0; index < remaining.Count; index++)
                {
                    var cost = model.Edge(previous, remaining[index]);
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestPosition = index;
                    }
                }

                var next = remaining[bestPosition];
                order.Add(next);
                remaining.RemoveAt(bestPosition);
                previous = next;
            }
        }

        return order;
    }

    /// <summary>
    /// 块间 ALNS：自适应破坏/修复 + 精英重启，最后用 VND 局部搜索收敛。
    /// </summary>
    private static List<int> OptimizeBlockOrder(
        BlockOrderModel model,
        List<int> initial,
        Random random,
        CancellationToken cancellationToken)
    {
        var count = model.Count;
        if (count < 3)
        {
            return initial;
        }

        var current = initial.ToList();
        var currentCost = OrderCost(current, model);
        var best = current.ToList();
        var bestCost = currentCost;
        var elite = new List<List<int>> { best.ToList() };
        var destroyWeights = new double[] { 1d, 1d, 1d, 1d, 1d };
        var repairWeights = new double[] { 1d, 1d };
        var destroyScores = new double[destroyWeights.Length];
        var destroyUses = new int[destroyWeights.Length];
        var repairScores = new double[repairWeights.Length];
        var repairUses = new int[repairWeights.Length];

        // 每次迭代只做 O(B) 的边权求和，因此可以承受更多迭代。
        var iterations = count <= 64 ? 240 : count <= 160 ? 160 : 90;
        var budget = Stopwatch.StartNew();
        var timeLimit = TimeSpan.FromMilliseconds(
            count <= 64 ? 120 : count <= 160 ? 180 : 220);
        var temperature = Math.Max(1d, bestCost * 0.03d);
        var lastImprovement = 0;

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            if (budget.Elapsed > timeLimit)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var destroyOperator = SelectWeightedOperator(random, destroyWeights);
            var repairOperator = SelectWeightedOperator(random, repairWeights);
            var candidate = current.ToList();
            var removed = DestroyBlocks(
                candidate,
                model,
                destroyOperator,
                random);
            if (removed.Count == 0)
            {
                continue;
            }

            RepairBlocks(candidate, removed, model, repairOperator, random);
            if (candidate.Count != count)
            {
                continue;
            }

            var candidateCost = OrderCost(candidate, model);
            var cooling = Math.Max(
                1e-6,
                temperature * (1d - (iteration / (double)iterations)));
            var accepted = candidateCost < currentCost ||
                           random.NextDouble() <
                           Math.Exp((currentCost - candidateCost) / cooling);
            if (accepted)
            {
                current = candidate;
                currentCost = candidateCost;
            }

            if (candidateCost + 1e-9 < bestCost)
            {
                best = candidate.ToList();
                bestCost = candidateCost;
                lastImprovement = iteration;
                elite.Insert(0, best.ToList());
                if (elite.Count > 8)
                {
                    elite.RemoveAt(elite.Count - 1);
                }

                destroyScores[destroyOperator] += 4d;
                repairScores[repairOperator] += 2d;
            }
            else if (accepted)
            {
                destroyScores[destroyOperator] += 1.5d;
                repairScores[repairOperator] += 1d;
            }
            else if (candidateCost < bestCost * 1.02d)
            {
                destroyScores[destroyOperator] += 0.5d;
            }

            destroyUses[destroyOperator]++;
            repairUses[repairOperator]++;
            if ((iteration + 1) % 25 == 0)
            {
                UpdateOperatorWeights(destroyWeights, destroyScores, destroyUses);
                UpdateOperatorWeights(repairWeights, repairScores, repairUses);
            }

            // 长期无改进时回到精英解并做一次扰动，避免过早收敛。
            if (iteration - lastImprovement > 70 && elite.Count > 0)
            {
                current = PerturbOrder(elite[random.Next(elite.Count)], random);
                currentCost = OrderCost(current, model);
                lastImprovement = iteration;
            }
        }

        return ImproveOrderLocal(best, model, cancellationToken);
    }

    /// <summary>
    /// 按权重选择算子。
    /// </summary>
    private static int SelectWeightedOperator(Random random, IReadOnlyList<double> weights)
    {
        var total = weights.Sum();
        var pick = random.NextDouble() * total;
        for (var index = 0; index < weights.Count; index++)
        {
            pick -= weights[index];
            if (pick <= 0)
            {
                return index;
            }
        }

        return weights.Count - 1;
    }

    /// <summary>
    /// 根据历史得分更新算子权重。
    /// </summary>
    private static void UpdateOperatorWeights(
        double[] weights,
        double[] scores,
        int[] uses)
    {
        for (var index = 0; index < weights.Length; index++)
        {
            weights[index] = Math.Max(
                0.1d,
                (0.7d * weights[index]) +
                (0.3d * (scores[index] / Math.Max(1, uses[index]))));
            scores[index] = 0d;
            uses[index] = 0;
        }
    }

    /// <summary>
    /// 破坏当前解，返回被移除的块。
    /// </summary>
    private static List<int> DestroyBlocks(
        List<int> candidate,
        BlockOrderModel model,
        int operatorIndex,
        Random random)
    {
        var count = candidate.Count;
        if (count == 0)
        {
            return [];
        }

        var removeCount = Math.Min(random.Next(2, 8), count);
        if (removeCount >= count)
        {
            removeCount = count - 1;
        }

        if (removeCount <= 0)
        {
            return [];
        }

        if (operatorIndex == 0)
        {
            var from = random.Next(0, candidate.Count - removeCount + 1);
            var removed = candidate.GetRange(from, removeCount);
            candidate.RemoveRange(from, removeCount);
            return removed;
        }

        if (operatorIndex == 1)
        {
            var removed = new List<int>(removeCount);
            for (var index = 0; index < removeCount; index++)
            {
                var at = random.Next(candidate.Count);
                removed.Add(candidate[at]);
                candidate.RemoveAt(at);
            }

            return removed;
        }

        if (operatorIndex == 2)
        {
            // worst：优先移除对当前路径贡献最大的块。
            var scored = candidate
                .Select((block, index) => (
                    block,
                    index,
                    score: MarginalBlockCost(candidate, model, index)))
                .OrderByDescending(item => item.score)
                .Take(removeCount)
                .ToList();
            var removed = scored.Select(item => item.block).ToList();
            foreach (var item in scored.OrderByDescending(item => item.index))
            {
                candidate.RemoveAt(item.index);
            }

            return removed;
        }

        if (operatorIndex == 3)
        {
            // related：移除种子块附近的块，集中打散一个区域。
            var seed = candidate[random.Next(candidate.Count)];
            var scored = candidate
                .Select((block, index) => (
                    block,
                    index,
                    score: BlockDistance(model.Blocks[seed], model.Blocks[block])))
                .OrderBy(item => item.score)
                .Take(removeCount)
                .ToList();
            var removed = scored.Select(item => item.block).ToList();
            foreach (var item in scored.OrderByDescending(item => item.index))
            {
                candidate.RemoveAt(item.index);
            }

            return removed;
        }

        // trip：移除同一传送点/仓库的整组块，允许路线数量发生变化。
        var teleportId = model.Blocks[candidate[random.Next(candidate.Count)]].Teleport.Id;
        var tripRemoved = new List<int>();
        for (var index = candidate.Count - 1; index >= 0; index--)
        {
            if (!string.Equals(
                    model.Blocks[candidate[index]].Teleport.Id,
                    teleportId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            tripRemoved.Add(candidate[index]);
            candidate.RemoveAt(index);
            if (tripRemoved.Count >= removeCount + 2)
            {
                break;
            }
        }

        return tripRemoved;
    }

    /// <summary>
    /// 计算移除指定块后路径代价的减少量。
    /// </summary>
    private static double MarginalBlockCost(
        IReadOnlyList<int> order,
        BlockOrderModel model,
        int index)
    {
        var block = order[index];
        var previous = index == 0 ? -1 : order[index - 1];
        var next = index + 1 == order.Count ? -1 : order[index + 1];
        return model.Edge(previous, block) +
               model.Edge(block, next) -
               model.Edge(previous, next);
    }

    /// <summary>
    /// 两个块之间的最小点距离，用作 related 破坏算子的相关性度量。
    /// </summary>
    private static double BlockDistance(WalkBlock left, WalkBlock right)
    {
        var best = double.PositiveInfinity;
        foreach (var first in left.Points)
        {
            foreach (var second in right.Points)
            {
                var distance = first.Marker.WorldPosition
                    .HorizontalDistanceTo(second.Marker.WorldPosition);
                if (distance < best)
                {
                    best = distance;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// 修复解：贪心插入或 regret-2 插入。
    /// </summary>
    private static void RepairBlocks(
        List<int> candidate,
        List<int> removed,
        BlockOrderModel model,
        int operatorIndex,
        Random random)
    {
        if (removed.Count == 0)
        {
            return;
        }

        if (operatorIndex == 1 && removed.Count > 1)
        {
            // regret-2：优先插入“次优位置也还可以”的块，避免最后被迫高代价插入。
            while (removed.Count > 0)
            {
                var bestRemoved = 0;
                var bestPosition = 0;
                var bestRegret = double.NegativeInfinity;
                for (var item = 0; item < removed.Count; item++)
                {
                    var block = removed[item];
                    var first = double.PositiveInfinity;
                    var second = double.PositiveInfinity;
                    var firstPosition = 0;
                    for (var position = 0; position <= candidate.Count; position++)
                    {
                        var delta = InsertionDelta(candidate, block, position, model);
                        if (delta < first)
                        {
                            second = first;
                            first = delta;
                            firstPosition = position;
                        }
                        else if (delta < second)
                        {
                            second = delta;
                        }
                    }

                    var regret = double.IsPositiveInfinity(second)
                        ? 0d
                        : second - first;
                    if (regret > bestRegret)
                    {
                        bestRegret = regret;
                        bestRemoved = item;
                        bestPosition = firstPosition;
                    }
                }

                candidate.Insert(bestPosition, removed[bestRemoved]);
                removed.RemoveAt(bestRemoved);
            }

            return;
        }

        // 贪心插入：每次选择当前代价最小的位置。
        foreach (var block in removed.ToList())
        {
            var bestPosition = 0;
            var bestDelta = double.PositiveInfinity;
            for (var position = 0; position <= candidate.Count; position++)
            {
                var delta = InsertionDelta(candidate, block, position, model);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    bestPosition = position;
                }
            }

            candidate.Insert(bestPosition, block);
            removed.Remove(block);
        }
    }

    /// <summary>
    /// 计算把块插入指定位置的边际代价。
    /// </summary>
    private static double InsertionDelta(
        IReadOnlyList<int> order,
        int block,
        int position,
        BlockOrderModel model)
    {
        if (order.Count == 0)
        {
            return model.Edge(-1, block);
        }

        if (position <= 0)
        {
            return model.Edge(-1, block) +
                   model.Edge(block, order[0]) -
                   model.Edge(-1, order[0]);
        }

        if (position >= order.Count)
        {
            return model.Edge(order[^1], block);
        }

        var previous = order[position - 1];
        var next = order[position];
        return model.Edge(previous, block) +
               model.Edge(block, next) -
               model.Edge(previous, next);
    }

    /// <summary>
    /// 精英解扰动：抽取一小段并重新插入。
    /// </summary>
    private static List<int> PerturbOrder(IReadOnlyList<int> order, Random random)
    {
        var perturbed = order.ToList();
        if (perturbed.Count < 3)
        {
            return perturbed;
        }

        var from = random.Next(perturbed.Count);
        var count = Math.Min(2, perturbed.Count - from);
        var segment = perturbed.GetRange(from, count);
        perturbed.RemoveRange(from, count);
        if (count > 0)
        {
            perturbed.InsertRange(random.Next(perturbed.Count + 1), segment);
        }

        return perturbed;
    }

    /// <summary>
    /// VND 局部搜索：relocate → swap → 2-opt，任一邻域改进后重新开始。
    /// </summary>
    private static List<int> ImproveOrderLocal(
        List<int> order,
        BlockOrderModel model,
        CancellationToken cancellationToken)
    {
        if (order.Count < 2)
        {
            return order;
        }

        var current = order.ToList();
        var currentCost = OrderCost(current, model);
        var budget = Stopwatch.StartNew();
        for (var pass = 0; pass < 24; pass++)
        {
            if (budget.ElapsedMilliseconds > 80)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (TryRelocate(current, model, ref currentCost))
            {
                continue;
            }

            if (TrySwap(current, model, ref currentCost))
            {
                continue;
            }

            if (current.Count <= MaxTwoOptBlocks &&
                TryTwoOpt(current, model, ref currentCost))
            {
                continue;
            }

            break;
        }

        return current;
    }

    /// <summary>
    /// 最佳改进的单块重定位。
    /// </summary>
    private static bool TryRelocate(
        List<int> order,
        BlockOrderModel model,
        ref double currentCost)
    {
        var count = order.Count;
        var bestDelta = -1e-9;
        var bestFrom = -1;
        var bestTo = -1;
        for (var from = 0; from < count; from++)
        {
            for (var to = 0; to < count; to++)
            {
                if (to == from)
                {
                    continue;
                }

                var delta = RelocateDelta(order, from, to, model);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    bestFrom = from;
                    bestTo = to;
                }
            }
        }

        if (bestFrom < 0)
        {
            return false;
        }

        var block = order[bestFrom];
        order.RemoveAt(bestFrom);
        order.Insert(bestTo, block);
        currentCost = OrderCost(order, model);
        return true;
    }

    /// <summary>
    /// 计算把 from 位置的块移动到 to 位置的代价变化；to 是移除该块后的插入下标。
    /// </summary>
    private static double RelocateDelta(
        IReadOnlyList<int> order,
        int from,
        int to,
        BlockOrderModel model)
    {
        var count = order.Count;
        if (from == to)
        {
            return 0d;
        }

        int AfterRemoval(int index) => index < from ? order[index] : order[index + 1];
        var block = order[from];
        var removedPrevious = from == 0 ? -1 : order[from - 1];
        var removedNext = from == count - 1 ? -1 : order[from + 1];
        var insertedPrevious = to == 0 ? -1 : AfterRemoval(to - 1);
        var insertedNext = to == count - 1 ? -1 : AfterRemoval(to);
        return model.Edge(removedPrevious, removedNext) -
               model.Edge(removedPrevious, block) -
               model.Edge(block, removedNext) +
               model.Edge(insertedPrevious, block) +
               model.Edge(block, insertedNext) -
               model.Edge(insertedPrevious, insertedNext);
    }

    /// <summary>
    /// 最佳改进的两块交换。
    /// </summary>
    private static bool TrySwap(
        List<int> order,
        BlockOrderModel model,
        ref double currentCost)
    {
        var bestDelta = -1e-9;
        var bestLeft = -1;
        var bestRight = -1;
        for (var left = 0; left < order.Count - 1; left++)
        {
            for (var right = left + 1; right < order.Count; right++)
            {
                var delta = SwapDelta(order, left, right, model);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    bestLeft = left;
                    bestRight = right;
                }
            }
        }

        if (bestLeft < 0)
        {
            return false;
        }

        (order[bestLeft], order[bestRight]) = (order[bestRight], order[bestLeft]);
        currentCost = OrderCost(order, model);
        return true;
    }

    /// <summary>
    /// 计算交换两个块后的代价变化。
    /// </summary>
    private static double SwapDelta(
        IReadOnlyList<int> order,
        int left,
        int right,
        BlockOrderModel model)
    {
        var x = order[left];
        var y = order[right];
        var previousLeft = left == 0 ? -1 : order[left - 1];
        var nextLeft = order[left + 1];
        var previousRight = order[right - 1];
        var nextRight = right == order.Count - 1 ? -1 : order[right + 1];
        if (right == left + 1)
        {
            return model.Edge(previousLeft, y) +
                   model.Edge(y, x) +
                   model.Edge(x, nextRight) -
                   model.Edge(previousLeft, x) -
                   model.Edge(x, y) -
                   model.Edge(y, nextRight);
        }

        return model.Edge(previousLeft, y) +
               model.Edge(y, nextLeft) +
               model.Edge(previousRight, x) +
               model.Edge(x, nextRight) -
               model.Edge(previousLeft, x) -
               model.Edge(x, nextLeft) -
               model.Edge(previousRight, y) -
               model.Edge(y, nextRight);
    }

    /// <summary>
    /// 最佳改进的 2-opt；只对较小块序启用，避免 O(B^3) 扫描吞掉时间预算。
    /// </summary>
    private static bool TryTwoOpt(
        List<int> order,
        BlockOrderModel model,
        ref double currentCost)
    {
        var bestDelta = -1e-9;
        var bestFrom = -1;
        var bestTo = -1;
        for (var from = 0; from < order.Count - 1; from++)
        {
            for (var to = from + 1; to < order.Count; to++)
            {
                order.Reverse(from, to - from + 1);
                var cost = OrderCost(order, model);
                order.Reverse(from, to - from + 1);
                var delta = cost - currentCost;
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    bestFrom = from;
                    bestTo = to;
                }
            }
        }

        if (bestFrom < 0)
        {
            return false;
        }

        order.Reverse(bestFrom, bestTo - bestFrom + 1);
        currentCost += bestDelta;
        return true;
    }

    /// <summary>
    /// 计算固定起点、开放终点的完整块序代价。
    /// </summary>
    private static double OrderCost(IReadOnlyList<int> order, BlockOrderModel model)
    {
        if (order.Count == 0)
        {
            return 0d;
        }

        var cost = model.Edge(-1, order[0]);
        for (var index = 1; index < order.Count; index++)
        {
            cost += model.Edge(order[index - 1], order[index]);
        }

        return cost;
    }

    /// <summary>
    /// 按块序生成最终路线，逐块比较步行进入和传送进入。
    /// </summary>
    private static MacroRoute BuildRoute(
        IReadOnlyList<WalkBlock> blocks,
        IReadOnlyList<int> order,
        MapPoint startPoint,
        bool hasExplicitStart,
        double teleportCost,
        CancellationToken cancellationToken)
    {
        var route = new MacroRoute { ClusterCount = blocks.Count };
        var maxWalkTransitionDistance = teleportCost <= 0d
            ? double.PositiveInfinity
            : MaxWalkTransitionDistance;
        var currentPoint = startPoint;
        var tripIndex = 0;
        var started = false;
        foreach (var index in order)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var block = blocks[index];
            var teleportRoute = block.TeleportRoute ??
                                throw new InvalidOperationException("传送路线尚未计算。");
            var walkRoute = BuildPointRoute(block, currentPoint, cancellationToken);
            var nearestBlockDistance = block.Points.Min(point =>
                currentPoint.HorizontalDistanceTo(point.Marker.WorldPosition));
            var forceLongTeleport = started &&
                                    nearestBlockDistance > maxWalkTransitionDistance;

            if (!started)
            {
                // 首次进入：有显式起点时比较步行和传送；无起点时视为已从传送点落地。
                var teleportEntry = teleportRoute.Cost +
                                    (hasExplicitStart ? teleportCost : 0d);
                var useTeleport = teleportEntry <= walkRoute.Cost;
                if (useTeleport && hasExplicitStart)
                {
                    AddTeleportLeg(
                        route,
                        currentPoint,
                        block.Teleport,
                        tripIndex,
                        teleportCost);
                }

                currentPoint = useTeleport
                    ? AppendPointRoute(route, block, teleportRoute, tripIndex, arrival: true)
                    : AppendPointRoute(route, block, walkRoute, tripIndex, arrival: false);
            }
            else
            {
                var teleportEntry = teleportRoute.Cost + teleportCost;
                var useTeleport = teleportEntry <= walkRoute.Cost ||
                                  (forceLongTeleport &&
                                   teleportEntry <= walkRoute.Cost * MaxWalkTransitionCostRatio);
                if (useTeleport)
                {
                    tripIndex++;
                    AddTeleportLeg(
                        route,
                        currentPoint,
                        block.Teleport,
                        tripIndex,
                        teleportCost);
                    currentPoint = AppendPointRoute(
                        route,
                        block,
                        teleportRoute,
                        tripIndex,
                        arrival: true);
                }
                else
                {
                    currentPoint = AppendPointRoute(
                        route,
                        block,
                        walkRoute,
                        tripIndex,
                        arrival: false);
                }
            }

            started = true;
        }

        return route;
    }

    /// <summary>
    /// 把块内路线追加到宏观路线，并返回新的当前位置。
    /// </summary>
    private static MapPoint AppendPointRoute(
        MacroRoute route,
        WalkBlock block,
        PointRoute plan,
        int tripIndex,
        bool arrival)
    {
        var current = plan.Start;
        for (var index = 0; index < plan.Order.Length; index++)
        {
            var marker = block.Points[plan.Order[index]].Marker;
            var distance = current.HorizontalDistanceTo(marker.WorldPosition);
            route.Legs.Add(new RouteLeg
            {
                From = current,
                To = marker.WorldPosition,
                Kind = arrival && index == 0
                    ? RouteLegKind.TeleportArrival
                    : RouteLegKind.EstimatedWalk,
                Distance = distance,
                Cost = distance,
                TripIndex = tripIndex
            });
            route.OrderedMarkers.Add(marker);
            route.TotalCost += distance;
            route.WalkingDistance += distance;
            current = marker.WorldPosition;
        }

        return current;
    }

    /// <summary>
    /// 追加一次纯传送跳转；绘制层会跳过该段，但代价和批次颜色仍生效。
    /// </summary>
    private static void AddTeleportLeg(
        MacroRoute route,
        MapPoint from,
        MapMarker teleport,
        int tripIndex,
        double teleportCost)
    {
        route.Legs.Add(new RouteLeg
        {
            From = from,
            To = teleport.WorldPosition,
            Kind = RouteLegKind.Teleport,
            Distance = 0,
            Cost = teleportCost,
            TripIndex = tripIndex
        });
        route.TotalCost += teleportCost;
        route.TeleportCount++;
    }

    /// <summary>
    /// 一个 trip 的原始信息，用于路线级拆分。
    /// </summary>
    private sealed class TripSnapshot
    {
        /// <summary>
        /// 原始 trip 索引。
        /// </summary>
        public int SourceTripIndex { get; init; }

        /// <summary>
        /// 原始腿。
        /// </summary>
        public List<RouteLeg> Legs { get; } = [];

        /// <summary>
        /// trip 内访问的玩法点，按访问顺序。
        /// </summary>
        public List<MapMarker> Markers { get; } = [];

        /// <summary>
        /// trip 开始处的传送腿。
        /// </summary>
        public List<RouteLeg> StartTeleportLegs { get; } = [];

        /// <summary>
        /// trip 起点。
        /// </summary>
        public MapPoint Start { get; set; }

        /// <summary>
        /// 第一个玩法点腿的类型。
        /// </summary>
        public RouteLegKind FirstMarkerKind { get; set; }

        /// <summary>
        /// trip 内纯步行距离。
        /// </summary>
        public double WalkCost { get; set; }

        /// <summary>
        /// 已确认可拆分的分组；为 null 表示保持原样。
        /// </summary>
        public List<(WalkBlock Block, PointRoute Plan)>? SplitGroups { get; set; }
    }

    /// <summary>
    /// 路线级拆分：trip 内点位分属多个传送点时，如果拆开后步行更省，则拆成多条 trip。
    /// 块级拆分使用近似估计，可能漏掉这种情况；这里用实际块内路线代价做最终判定。
    /// </summary>
    private static MacroRoute SplitTripsByTeleport(
        MacroRoute route,
        IReadOnlyList<MapMarker> layerTeleports,
        IReadOnlyList<MapMarker> allTeleports,
        string? layerId,
        double fallbackDistance,
        double teleportCost,
        CancellationToken cancellationToken)
    {
        if (route.Legs.Count == 0 ||
            route.OrderedMarkers.Count == 0 ||
            allTeleports.Count <= 1 ||
            route.Legs.Count(leg => leg.Kind != RouteLegKind.Teleport) != route.OrderedMarkers.Count)
        {
            return route;
        }

        var trips = ExtractTrips(route);
        // 先处理小 trip，保证 24 点级别的异常优先修复；大 trip 在时间预算内继续尝试。
        var splitBudget = Stopwatch.StartNew();
        foreach (var trip in trips.OrderBy(item => item.Markers.Count))
        {
            if (splitBudget.ElapsedMilliseconds > SplitTripBudgetMilliseconds)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (TrySplitTrip(
                    trip,
                    layerTeleports,
                    allTeleports,
                    layerId,
                    fallbackDistance,
                    teleportCost,
                    cancellationToken,
                    out var splitGroups))
            {
                trip.SplitGroups = splitGroups;
            }
        }

        var rebuilt = new MacroRoute { ClusterCount = route.ClusterCount };
        var tripIndex = 0;
        var changed = false;
        foreach (var trip in trips)
        {
            if (trip.SplitGroups is { } splitGroups)
            {
                changed = true;
                foreach (var leg in trip.StartTeleportLegs)
                {
                    CopyLeg(rebuilt, leg, tripIndex);
                }

                var first = splitGroups[0];
                var previousEnd = AppendPointRoute(
                    rebuilt,
                    first.Block,
                    first.Plan,
                    tripIndex,
                    arrival: trip.FirstMarkerKind == RouteLegKind.TeleportArrival);
                for (var index = 1; index < splitGroups.Count; index++)
                {
                    tripIndex++;
                    var group = splitGroups[index];
                    AddTeleportLeg(
                        rebuilt,
                        previousEnd,
                        group.Block.Teleport,
                        tripIndex,
                        teleportCost);
                    previousEnd = AppendPointRoute(
                        rebuilt,
                        group.Block,
                        group.Plan,
                        tripIndex,
                        arrival: true);
                }

                tripIndex++;
                continue;
            }

            foreach (var leg in trip.Legs)
            {
                CopyLeg(rebuilt, leg, tripIndex);
            }

            rebuilt.OrderedMarkers.AddRange(trip.Markers);
            tripIndex++;
        }

        if (!changed)
        {
            return route;
        }

        route.Legs = rebuilt.Legs;
        route.OrderedMarkers = rebuilt.OrderedMarkers;
        route.TotalCost = rebuilt.TotalCost;
        route.WalkingDistance = rebuilt.WalkingDistance;
        route.TeleportCount = rebuilt.TeleportCount;
        return route;
    }

    /// <summary>
    /// 按 trip 索引提取原始路线。
    /// </summary>
    private static List<TripSnapshot> ExtractTrips(MacroRoute route)
    {
        var trips = new List<TripSnapshot>();
        TripSnapshot? current = null;
        var markerIndex = 0;
        foreach (var leg in route.Legs)
        {
            if (current is null || current.SourceTripIndex != leg.TripIndex)
            {
                current = new TripSnapshot { SourceTripIndex = leg.TripIndex };
                trips.Add(current);
            }

            current.Legs.Add(leg);
            if (leg.Kind == RouteLegKind.Teleport)
            {
                if (current.Markers.Count == 0)
                {
                    current.StartTeleportLegs.Add(leg);
                }

                continue;
            }

            var marker = route.OrderedMarkers[markerIndex++];
            current.Markers.Add(marker);
            current.WalkCost += leg.Distance;
            if (current.Markers.Count == 1)
            {
                current.Start = leg.From;
                current.FirstMarkerKind = leg.Kind;
            }
        }

        return trips;
    }

    /// <summary>
    /// 尝试把一个 trip 按传送点拆分；只有拆分后的步行 + 额外传送代价更低时才接受。
    /// </summary>
    private static bool TrySplitTrip(
        TripSnapshot trip,
        IReadOnlyList<MapMarker> layerTeleports,
        IReadOnlyList<MapMarker> allTeleports,
        string? layerId,
        double fallbackDistance,
        double teleportCost,
        CancellationToken cancellationToken,
        out List<(WalkBlock Block, PointRoute Plan)> splitGroups)
    {
        splitGroups = [];
        if (trip.Markers.Count < 2 || trip.Markers.Count > MaxRouteSplitPoints)
        {
            return false;
        }

        var planned = trip.Markers
            .Select(marker => CreatePlannedPoint(
                marker,
                layerTeleports,
                allTeleports,
                layerId,
                fallbackDistance))
            .ToList();
        var groups = planned
            .GroupBy(point => point.Teleport.Id, StringComparer.Ordinal)
            .Select(group => group.ToList())
            .ToList();
        if (groups.Count <= 1 || groups.Count > MaxRouteSplitGroups)
        {
            return false;
        }

        // 保持原 trip 的第一个点作为第一个分组，避免改变起点。
        var firstGroupIndex = groups.FindIndex(group =>
            ReferenceEquals(group[0].Marker, trip.Markers[0]));
        if (firstGroupIndex > 0)
        {
            var firstGroup = groups[firstGroupIndex];
            groups.RemoveAt(firstGroupIndex);
            groups.Insert(0, firstGroup);
        }

        var result = new List<(WalkBlock Block, PointRoute Plan)>(groups.Count);
        var firstBlock = new WalkBlock(groups[0], groups[0][0].Teleport);
        var firstPlan = BuildPointRoute(firstBlock, trip.Start, cancellationToken);
        result.Add((firstBlock, firstPlan));
        var splitCost = firstPlan.Cost;
        for (var index = 1; index < groups.Count; index++)
        {
            var block = new WalkBlock(groups[index], groups[index][0].Teleport);
            var plan = BuildPointRoute(
                block,
                block.Teleport.WorldPosition,
                cancellationToken);
            result.Add((block, plan));
            splitCost += plan.Cost + teleportCost;
        }

        if (splitCost + 1e-9 >= trip.WalkCost)
        {
            return false;
        }

        splitGroups = result;
        return true;
    }

    /// <summary>
    /// 复制一条腿并更新统计。
    /// </summary>
    private static void CopyLeg(MacroRoute route, RouteLeg leg, int tripIndex)
    {
        route.Legs.Add(new RouteLeg
        {
            From = leg.From,
            To = leg.To,
            Kind = leg.Kind,
            Distance = leg.Distance,
            Cost = leg.Cost,
            TripIndex = tripIndex
        });
        route.TotalCost += leg.Cost;
        if (leg.Kind == RouteLegKind.Teleport)
        {
            route.TeleportCount++;
        }
        else
        {
            route.WalkingDistance += leg.Distance;
        }
    }

    /// <summary>
    /// 获取分类本身及其全部子分类。
    /// </summary>
    private static HashSet<string> GetCategoryIds(MapSnapshot snapshot, string categoryId)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal) { categoryId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var category in snapshot.Categories)
            {
                if (category.ParentId is { } parentId &&
                    ids.Contains(parentId) &&
                    ids.Add(category.Id))
                {
                    changed = true;
                }
            }
        }

        return ids;
    }

    /// <summary>
    /// Held-Karp 动态规划求从固定起点访问全部点的最短开放路径。
    /// 仅用于小规模行程，避免指数级算法影响大地图。
    /// </summary>
    private static List<MapMarker> BuildExactOrder(
        MapPoint start,
        IReadOnlyList<MapMarker> markers,
        RouteRequest request)
    {
        var count = markers.Count;
        if (count <= 1)
        {
            return markers.ToList();
        }

        var stateCount = 1 << count;
        var size = stateCount * count;
        var costs = ArrayPool<double>.Shared.Rent(size);
        var previous = ArrayPool<sbyte>.Shared.Rent(size);
        var distances = ArrayPool<double>.Shared.Rent(count * count);
        try
        {
            Array.Fill(costs, double.PositiveInfinity, 0, size);
            Array.Fill(previous, (sbyte)-1, 0, size);
            for (var left = 0; left < count; left++)
            {
                for (var right = 0; right < count; right++)
                {
                    distances[(left * count) + right] =
                        markers[left].WorldPosition.HorizontalDistanceTo(
                            markers[right].WorldPosition) *
                        request.WalkCostFactor;
                }
            }

            for (var index = 0; index < count; index++)
            {
                costs[((1 << index) * count) + index] =
                    EdgeCost(start, markers[index], request);
            }

            var fullState = stateCount - 1;
            for (var state = 1; state < fullState; state++)
            {
                var remaining = fullState ^ state;
                var lastBits = state;
                while (lastBits != 0)
                {
                    var last = BitOperations.TrailingZeroCount(lastBits);
                    lastBits &= lastBits - 1;
                    var current = costs[(state * count) + last];
                    if (double.IsPositiveInfinity(current))
                    {
                        continue;
                    }

                    var nextBits = remaining;
                    while (nextBits != 0)
                    {
                        var next = BitOperations.TrailingZeroCount(nextBits);
                        nextBits &= nextBits - 1;
                        var nextState = state | (1 << next);
                        var index = (nextState * count) + next;
                        var cost = current +
                                   distances[(last * count) + next] +
                                   Penalty(markers[next], request);
                        if (cost < costs[index])
                        {
                            costs[index] = cost;
                            previous[index] = (sbyte)last;
                        }
                    }
                }
            }

            var bestLast = 0;
            for (var index = 1; index < count; index++)
            {
                if (costs[(fullState * count) + index] <
                    costs[(fullState * count) + bestLast])
                {
                    bestLast = index;
                }
            }

            if (double.IsPositiveInfinity(costs[(fullState * count) + bestLast]))
            {
                return BuildNearestOrder(start, markers, request);
            }

            var order = new List<MapMarker>(count);
            var stateAt = fullState;
            var currentIndex = bestLast;
            while (currentIndex >= 0)
            {
                order.Add(markers[currentIndex]);
                var prior = previous[(stateAt * count) + currentIndex];
                stateAt &= ~(1 << currentIndex);
                currentIndex = prior;
            }

            order.Reverse();
            return order;
        }
        finally
        {
            ArrayPool<double>.Shared.Return(costs);
            ArrayPool<sbyte>.Shared.Return(previous);
            ArrayPool<double>.Shared.Return(distances);
        }
    }

    /// <summary>
    /// 用最近邻构造初始访问顺序。
    /// </summary>
    private static List<MapMarker> BuildNearestOrder(
        MapPoint start,
        IReadOnlyList<MapMarker> markers,
        RouteRequest request)
    {
        var remaining = markers.ToList();
        var order = new List<MapMarker>(remaining.Count);
        var current = start;

        while (remaining.Count > 0)
        {
            var bestIndex = 0;
            var bestCost = double.PositiveInfinity;
            for (var index = 0; index < remaining.Count; index++)
            {
                var marker = remaining[index];
                var cost = EdgeCost(current, marker, request);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestIndex = index;
                }
            }

            var next = remaining[bestIndex];
            order.Add(next);
            current = next.WorldPosition;
            remaining.RemoveAt(bestIndex);
        }

        return order;
    }

    /// <summary>
    /// 用 2-opt 改进访问顺序，起点保持固定。
    /// </summary>
    private static List<MapMarker> ImproveOrder(
        MapPoint start,
        List<MapMarker> order,
        RouteRequest request)
    {
        if (order.Count < 3)
        {
            return order;
        }

        var improved = true;
        var iterations = 0;
        while (improved && iterations < 8)
        {
            improved = false;
            iterations++;

            for (var from = 0; from < order.Count - 1; from++)
            {
                for (var to = from + 1; to < order.Count; to++)
                {
                    var currentCost = SegmentCost(start, order, from, to, request);
                    order.Reverse(from, to - from + 1);
                    var reversedCost = SegmentCost(start, order, from, to, request);
                    if (reversedCost + 1e-9 < currentCost)
                    {
                        improved = true;
                    }
                    else
                    {
                        order.Reverse(from, to - from + 1);
                    }
                }
            }
        }

        // 2-opt 之后再做 Or-opt，覆盖 3-opt 的相邻段移动邻域。
        return ImproveOrderWithOrOpt(start, order, request);
    }

    /// <summary>
    /// Or-opt：移动长度 1~3 的连续段，并可反向，作为 3-opt 的实用子集。
    /// </summary>
    private static List<MapMarker> ImproveOrderWithOrOpt(
        MapPoint start,
        List<MapMarker> order,
        RouteRequest request)
    {
        if (order.Count < 4 || order.Count > 64)
        {
            return order;
        }

        var bestCost = OrderCost(start, order, request);
        for (var pass = 0; pass < 4; pass++)
        {
            var improved = false;
            for (var length = 1; length <= 3 && length < order.Count; length++)
            {
                for (var from = 0; from + length <= order.Count; from++)
                {
                    var segment = order.GetRange(from, length);
                    var without = order.ToList();
                    without.RemoveRange(from, length);
                    for (var position = 0; position <= without.Count; position++)
                    {
                        for (var reverse = 0; reverse < 2; reverse++)
                        {
                            if (reverse == 1 && segment.Count < 2)
                            {
                                continue;
                            }

                            var trial = without.ToList();
                            var inserted = reverse == 0
                                ? segment
                                : Enumerable.Reverse(segment).ToList();
                            trial.InsertRange(position, inserted);
                            var cost = OrderCost(start, trial, request);
                            if (cost + 1e-9 < bestCost)
                            {
                                order = trial;
                                bestCost = cost;
                                improved = true;
                                break;
                            }
                        }

                        if (improved)
                        {
                            break;
                        }
                    }

                    if (improved)
                    {
                        break;
                    }
                }

                if (improved)
                {
                    break;
                }
            }

            if (!improved)
            {
                break;
            }
        }

        return order;
    }

    /// <summary>
    /// 计算固定起点、开放终点的完整路线代价。
    /// </summary>
    private static double OrderCost(
        MapPoint start,
        IReadOnlyList<MapMarker> order,
        RouteRequest request)
    {
        var current = start;
        var cost = 0d;
        foreach (var marker in order)
        {
            cost += EdgeCost(current, marker, request);
            current = marker.WorldPosition;
        }

        return cost;
    }

    /// <summary>
    /// 计算反转区间前后的局部代价。
    /// 目的地传送罚分在整条路线中只与访问集合有关，反转不改变总和，
    /// 因此只需比较边界边。
    /// </summary>
    private static double SegmentCost(
        MapPoint start,
        IReadOnlyList<MapMarker> order,
        int from,
        int to,
        RouteRequest request)
    {
        var previous = from == 0 ? start : order[from - 1].WorldPosition;
        var cost = EdgeCost(previous, order[from], request);
        cost += EdgeCost(order[to].WorldPosition,
            to + 1 < order.Count ? order[to + 1].WorldPosition : order[to].WorldPosition,
            request);
        return cost;
    }

    /// <summary>
    /// 计算移动到指定标记的代价。
    /// </summary>
    private static double EdgeCost(MapPoint from, MapMarker marker, RouteRequest request)
    {
        return from.HorizontalDistanceTo(marker.WorldPosition) * request.WalkCostFactor +
               Penalty(marker, request);
    }

    /// <summary>
    /// 计算从一个坐标到另一个坐标的代价。
    /// </summary>
    private static double EdgeCost(MapPoint from, MapPoint to, RouteRequest request)
    {
        return from.HorizontalDistanceTo(to) * request.WalkCostFactor;
    }

    /// <summary>
    /// 返回标记的传送罚分。
    /// </summary>
    private static double Penalty(MapMarker marker, RouteRequest request)
    {
        return marker.IsTeleport ? request.TeleportPenalty : 0d;
    }
}
