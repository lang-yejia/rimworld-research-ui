using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ResearchReframed;

internal sealed class ResearchEdgeRoute
{
    internal ResearchEdgeRoute(
        ResearchProjectDef from,
        ResearchProjectDef to,
        IReadOnlyList<Vector2> points)
    {
        From = from;
        To = to;
        Points = points;
    }

    internal ResearchProjectDef From { get; }
    internal ResearchProjectDef To { get; }
    internal IReadOnlyList<Vector2> Points { get; }
}

internal static class ResearchEdgeRouter
{
    private const float LaneSpacing = 7f;
    private const float ObstacleMargin = 15f;
    private static readonly Dictionary<ResearchTabDef, CacheEntry> Cache = new();

    internal static IReadOnlyList<ResearchEdgeRoute> Build(
        ResearchTabDef? tab,
        IReadOnlyCollection<ResearchProjectDef> projects,
        IReadOnlyDictionary<ResearchProjectDef, Rect> layout)
    {
        if (tab == null || projects.Count == 0)
        {
            return Array.Empty<ResearchEdgeRoute>();
        }

        var signature = Signature(projects, layout);
        if (!Cache.TryGetValue(tab, out var entry) || entry.Signature != signature)
        {
            entry = new CacheEntry(signature, Route(projects, layout));
            Cache[tab] = entry;
        }
        return entry.Routes;
    }

    private static IReadOnlyList<ResearchEdgeRoute> Route(
        IReadOnlyCollection<ResearchProjectDef> projects,
        IReadOnlyDictionary<ResearchProjectDef, Rect> layout)
    {
        var projectSet = new HashSet<ResearchProjectDef>(projects);
        var columnXs = layout.Values.Select(rect => rect.x).Distinct().OrderBy(x => x).ToList();
        var columnOf = layout.ToDictionary(pair => pair.Key, pair => columnXs.IndexOf(pair.Value.x));
        var columnNodes = columnXs.Select((_, column) => layout
                .Where(pair => columnOf[pair.Key] == column)
                .Select(pair => pair.Value)
                .OrderBy(rect => rect.y)
                .ToList())
            .ToList();

        var edges = projects
            .SelectMany(to => (to.prerequisites ?? new List<ResearchProjectDef>())
                .Where(projectSet.Contains)
                .Select(from => (from, to)))
            .Where(edge => columnOf[edge.from] < columnOf[edge.to])
            .OrderBy(edge => columnOf[edge.from])
            .ThenBy(edge => layout[edge.from].center.y)
            .ThenBy(edge => columnOf[edge.to])
            .ThenBy(edge => layout[edge.to].center.y)
            .ThenBy(edge => edge.from.defName)
            .ThenBy(edge => edge.to.defName)
            .ToList();

        var gapTotals = new Dictionary<int, int>();
        foreach (var edge in edges)
        {
            for (var gap = columnOf[edge.from]; gap < columnOf[edge.to]; gap++)
            {
                gapTotals[gap] = gapTotals.TryGetValue(gap, out var count) ? count + 1 : 1;
            }
        }
        var gapUsed = gapTotals.Keys.ToDictionary(gap => gap, _ => 0);

        var outgoing = projects.ToDictionary(project => project, _ => new List<ResearchProjectDef>());
        var incoming = projects.ToDictionary(project => project, _ => new List<ResearchProjectDef>());
        foreach (var edge in edges)
        {
            outgoing[edge.from].Add(edge.to);
            incoming[edge.to].Add(edge.from);
        }
        foreach (var list in outgoing.Values)
        {
            list.Sort((left, right) => layout[left].center.y.CompareTo(layout[right].center.y));
        }
        foreach (var list in incoming.Values)
        {
            list.Sort((left, right) => layout[left].center.y.CompareTo(layout[right].center.y));
        }

        var maximumY = layout.Values.Max(rect => rect.yMax) + 35f;
        var routes = new List<ResearchEdgeRoute>();
        foreach (var edge in edges)
        {
            var fromRect = layout[edge.from];
            var toRect = layout[edge.to];
            var fromColumn = columnOf[edge.from];
            var toColumn = columnOf[edge.to];
            var startY = PortY(fromRect, outgoing[edge.from].IndexOf(edge.to), outgoing[edge.from].Count);
            var endY = PortY(toRect, incoming[edge.to].IndexOf(edge.from), incoming[edge.to].Count);
            var points = new List<Vector2>();
            AddPoint(points, new Vector2(fromRect.xMax, startY));

            var currentY = startY;
            for (var gap = fromColumn; gap < toColumn; gap++)
            {
                var ordinal = gapUsed[gap]++;
                var trackCount = Mathf.Min(gapTotals[gap], 9);
                var track = ordinal % trackCount;
                var laneOffset = (track - (trackCount - 1) * 0.5f) * LaneSpacing;
                var corridorX = (columnXs[gap] + ResearchGraphLayout.NodeWidth + columnXs[gap + 1]) * 0.5f + laneOffset;
                AddPoint(points, new Vector2(corridorX, currentY));

                if (gap == toColumn - 1)
                {
                    AddPoint(points, new Vector2(corridorX, endY));
                    AddPoint(points, new Vector2(toRect.xMin, endY));
                    continue;
                }

                var progress = (gap - fromColumn + 1f) / (toColumn - fromColumn);
                var desiredY = Mathf.Lerp(startY, endY, progress) + laneOffset * 0.35f;
                currentY = FindFreeY(desiredY, columnNodes[gap + 1], maximumY);
                AddPoint(points, new Vector2(corridorX, currentY));
            }

            routes.Add(new ResearchEdgeRoute(edge.from, edge.to, points));
        }
        return routes;
    }

    private static float FindFreeY(float desired, IReadOnlyList<Rect> obstacles, float maximumY)
    {
        var y = Mathf.Clamp(desired, 12f, maximumY);
        for (var pass = 0; pass < obstacles.Count + 2; pass++)
        {
            var collision = obstacles.FirstOrDefault(rect =>
                y >= rect.yMin - ObstacleMargin && y <= rect.yMax + ObstacleMargin);
            if (collision.width <= 0f)
            {
                break;
            }

            var above = collision.yMin - ObstacleMargin;
            var below = collision.yMax + ObstacleMargin;
            y = Mathf.Abs(y - above) <= Mathf.Abs(y - below) ? above : below;
            y = Mathf.Clamp(y, 12f, maximumY);
        }
        return y;
    }

    private static float PortY(Rect node, int index, int count)
    {
        if (count <= 1)
        {
            return node.center.y;
        }

        var usableHeight = node.height * 0.55f;
        return node.center.y - usableHeight * 0.5f + usableHeight * index / (count - 1);
    }

    private static void AddPoint(IList<Vector2> points, Vector2 point)
    {
        point = new Vector2(Snap(point.x), Snap(point.y));
        if (points.Count > 0 && Vector2.Distance(points[points.Count - 1], point) < 0.1f)
        {
            return;
        }

        if (points.Count >= 2)
        {
            var previous = points[points.Count - 1];
            var beforePrevious = points[points.Count - 2];
            var sameHorizontal = Mathf.Abs(beforePrevious.y - previous.y) < 0.1f &&
                                 Mathf.Abs(previous.y - point.y) < 0.1f;
            var sameVertical = Mathf.Abs(beforePrevious.x - previous.x) < 0.1f &&
                               Mathf.Abs(previous.x - point.x) < 0.1f;
            if (sameHorizontal || sameVertical)
            {
                points[points.Count - 1] = point;
                return;
            }
        }
        points.Add(point);
    }

    private static float Snap(float value) => Mathf.Round(value) + 0.5f;

    private static string Signature(
        IEnumerable<ResearchProjectDef> projects,
        IReadOnlyDictionary<ResearchProjectDef, Rect> layout)
    {
        return string.Join("|", projects.OrderBy(project => project.defName).Select(project =>
        {
            var rect = layout[project];
            var prerequisites = string.Join(",", (project.prerequisites ?? new List<ResearchProjectDef>())
                .Select(prerequisite => prerequisite.defName).OrderBy(name => name));
            return $"{project.defName}:{rect.x:0}:{rect.y:0}:{prerequisites}";
        }));
    }

    private sealed class CacheEntry
    {
        internal CacheEntry(string signature, IReadOnlyList<ResearchEdgeRoute> routes)
        {
            Signature = signature;
            Routes = routes;
        }

        internal string Signature { get; }
        internal IReadOnlyList<ResearchEdgeRoute> Routes { get; }
    }
}
