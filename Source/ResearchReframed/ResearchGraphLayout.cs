using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ResearchReframed;

internal static class ResearchGraphLayout
{
    internal const float NodeWidth = 232f;
    internal const float NodeHeight = 124f;
    internal const float ColumnSpacing = 350f;
    internal const float RowSpacing = 184f;

    private const float Padding = 42f;
    private const int SweepCount = 8;
    private static readonly Dictionary<ResearchTabDef, CacheEntry> Cache = new();

    internal static Dictionary<ResearchProjectDef, Rect> Build(
        ResearchTabDef? tab,
        IReadOnlyCollection<ResearchProjectDef> projects,
        float scale)
    {
        if (tab == null || projects.Count == 0)
        {
            return new Dictionary<ResearchProjectDef, Rect>();
        }

        var signature = Signature(projects);
        if (!Cache.TryGetValue(tab, out var entry) || entry.Signature != signature)
        {
            entry = new CacheEntry(signature, BuildBaseLayout(projects));
            Cache[tab] = entry;
        }

        return entry.Rectangles.ToDictionary(
            pair => pair.Key,
            pair => new Rect(pair.Value.x * scale, pair.Value.y * scale,
                pair.Value.width * scale, pair.Value.height * scale));
    }

    private static Dictionary<ResearchProjectDef, Rect> BuildBaseLayout(
        IReadOnlyCollection<ResearchProjectDef> projects)
    {
        var projectSet = new HashSet<ResearchProjectDef>(projects);
        var parents = projects.ToDictionary(
            project => project,
            project => (project.prerequisites ?? new List<ResearchProjectDef>())
                .Where(projectSet.Contains)
                .ToList());
        var children = projects.ToDictionary(project => project, _ => new List<ResearchProjectDef>());
        foreach (var project in projects)
        {
            foreach (var parent in parents[project])
            {
                children[parent].Add(project);
            }
        }

        var ranks = new Dictionary<ResearchProjectDef, int>();
        foreach (var project in projects.OrderBy(project => project.defName))
        {
            AssignRank(project, parents, ranks, new HashSet<ResearchProjectDef>());
        }

        var rankValues = ranks.Values.Distinct().OrderBy(rank => rank).ToList();
        var compressedRanks = rankValues.Select((rank, index) => (rank, index))
            .ToDictionary(pair => pair.rank, pair => pair.index);
        foreach (var project in projects)
        {
            ranks[project] = compressedRanks[ranks[project]];
        }

        var columns = ranks.GroupBy(pair => pair.Value)
            .ToDictionary(
                group => group.Key,
                group => group.Select(pair => pair.Key)
                    .OrderBy(project => project.researchViewY)
                    .ThenBy(project => project.defName)
                    .ToList());
        var maximumRank = columns.Keys.Max();

        for (var sweep = 0; sweep < SweepCount; sweep++)
        {
            for (var rank = 1; rank <= maximumRank; rank++)
            {
                if (columns.TryGetValue(rank, out var column))
                {
                    SortByNeighbours(column, parents, columns, ranks);
                }
            }

            for (var rank = maximumRank - 1; rank >= 0; rank--)
            {
                if (columns.TryGetValue(rank, out var column))
                {
                    SortByNeighbours(column, children, columns, ranks);
                }
            }
        }

        var largestColumn = columns.Values.Max(column => column.Count);
        var result = new Dictionary<ResearchProjectDef, Rect>();
        foreach (var pair in columns.OrderBy(pair => pair.Key))
        {
            var column = pair.Value;
            var verticalOffset = (largestColumn - column.Count) * RowSpacing * 0.5f;
            for (var index = 0; index < column.Count; index++)
            {
                result[column[index]] = new Rect(
                    Padding + pair.Key * ColumnSpacing,
                    Padding + verticalOffset + index * RowSpacing,
                    NodeWidth,
                    NodeHeight);
            }
        }

        return result;
    }

    private static int AssignRank(
        ResearchProjectDef project,
        IReadOnlyDictionary<ResearchProjectDef, List<ResearchProjectDef>> parents,
        IDictionary<ResearchProjectDef, int> ranks,
        ISet<ResearchProjectDef> visiting)
    {
        if (ranks.TryGetValue(project, out var cached))
        {
            return cached;
        }

        var preferred = Mathf.Max(0, Mathf.RoundToInt(project.researchViewX));
        if (!visiting.Add(project))
        {
            return preferred;
        }

        var required = 0;
        foreach (var parent in parents[project])
        {
            required = Mathf.Max(required, AssignRank(parent, parents, ranks, visiting) + 1);
        }
        visiting.Remove(project);

        var rank = Mathf.Max(preferred, required);
        ranks[project] = rank;
        return rank;
    }

    private static void SortByNeighbours(
        List<ResearchProjectDef> column,
        IReadOnlyDictionary<ResearchProjectDef, List<ResearchProjectDef>> neighbours,
        IReadOnlyDictionary<int, List<ResearchProjectDef>> columns,
        IReadOnlyDictionary<ResearchProjectDef, int> ranks)
    {
        var oldOrder = column.Select((project, index) => (project, index))
            .ToDictionary(pair => pair.project, pair => pair.index);

        column.Sort((left, right) =>
        {
            var comparison = Barycenter(left, neighbours, columns, ranks, oldOrder)
                .CompareTo(Barycenter(right, neighbours, columns, ranks, oldOrder));
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = oldOrder[left].CompareTo(oldOrder[right]);
            return comparison != 0
                ? comparison
                : string.Compare(left.defName, right.defName, StringComparison.Ordinal);
        });
    }

    private static float Barycenter(
        ResearchProjectDef project,
        IReadOnlyDictionary<ResearchProjectDef, List<ResearchProjectDef>> neighbours,
        IReadOnlyDictionary<int, List<ResearchProjectDef>> columns,
        IReadOnlyDictionary<ResearchProjectDef, int> ranks,
        IReadOnlyDictionary<ResearchProjectDef, int> fallback)
    {
        var positions = new List<float>();
        foreach (var neighbour in neighbours[project])
        {
            var neighbourColumn = columns[ranks[neighbour]];
            positions.Add(neighbourColumn.IndexOf(neighbour));
        }

        return positions.Count == 0 ? fallback[project] : positions.Average();
    }

    private static string Signature(IEnumerable<ResearchProjectDef> projects)
    {
        return string.Join("|", projects.OrderBy(project => project.defName).Select(project =>
            $"{project.defName}:{project.researchViewX:0.###}:{project.researchViewY:0.###}:" +
            string.Join(",", (project.prerequisites ?? new List<ResearchProjectDef>())
                .Select(prerequisite => prerequisite.defName).OrderBy(name => name))));
    }

    private sealed class CacheEntry
    {
        internal CacheEntry(string signature, Dictionary<ResearchProjectDef, Rect> rectangles)
        {
            Signature = signature;
            Rectangles = rectangles;
        }

        internal string Signature { get; }
        internal Dictionary<ResearchProjectDef, Rect> Rectangles { get; }
    }
}
