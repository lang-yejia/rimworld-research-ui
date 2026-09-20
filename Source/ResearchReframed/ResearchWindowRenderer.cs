using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ResearchReframed;

internal static class ResearchWindowRenderer
{
    private const float HeaderHeight = 82f;
    private const float SidebarWidth = 310f;
    private const float NodeWidth = 196f;
    private const float NodeHeight = 68f;
    private const float ColumnWidth = 232f;
    private const float RowHeight = 92f;

    private static readonly Color Background = new(0.105f, 0.115f, 0.12f);
    private static readonly Color Panel = new(0.16f, 0.17f, 0.18f);
    private static readonly Color Completed = new(0.24f, 0.42f, 0.29f);
    private static readonly Color Current = new(0.55f, 0.43f, 0.20f);
    private static readonly Color Available = new(0.25f, 0.31f, 0.35f);
    private static readonly Color Locked = new(0.18f, 0.19f, 0.20f);
    private static readonly Color Connection = new(0.45f, 0.46f, 0.43f, 0.8f);

    private static Vector2 graphScroll;
    private static Vector2 tabScroll;
    private static Vector2 detailsScroll;
    private static ResearchTabDef? selectedTab;
    private static ResearchProjectDef? selectedProject;
    private static string search = string.Empty;

    public static void Draw(Rect rect)
    {
        Widgets.DrawBoxSolid(rect, Background);

        var header = new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, HeaderHeight - 12f);
        DrawHeader(header);

        var bodyY = rect.y + HeaderHeight;
        var graphRect = new Rect(rect.x + 10f, bodyY, rect.width - SidebarWidth - 30f, rect.height - HeaderHeight - 10f);
        var detailsRect = new Rect(graphRect.xMax + 10f, bodyY, SidebarWidth, graphRect.height);

        DrawGraph(graphRect);
        DrawDetails(detailsRect);
    }

    private static void DrawHeader(Rect rect)
    {
        var tabs = DefDatabase<ResearchTabDef>.AllDefsListForReading
            .OrderBy(tab => tab.label)
            .ToList();

        if (selectedTab == null || !tabs.Contains(selectedTab))
        {
            selectedTab = tabs.FirstOrDefault();
        }

        var searchRect = new Rect(rect.xMax - 250f, rect.y, 250f, 30f);
        search = Widgets.TextField(searchRect, search);
        if (search.NullOrEmpty())
        {
            GUI.color = new Color(1f, 1f, 1f, 0.45f);
            Widgets.Label(searchRect.ContractedBy(6f, 3f), "RR_Search".Translate());
            GUI.color = Color.white;
        }

        var tabArea = new Rect(rect.x, rect.y, searchRect.x - rect.x - 12f, 42f);
        var tabWidths = tabs.Select(tab => Mathf.Clamp(Text.CalcSize(tab.LabelCap).x + 24f, 90f, 190f)).ToList();
        var totalTabWidth = Mathf.Max(tabArea.width - 16f, tabWidths.Sum() + Mathf.Max(0, tabs.Count - 1) * 5f);
        var tabView = new Rect(0f, 0f, totalTabWidth, 30f);
        Widgets.BeginScrollView(tabArea, ref tabScroll, tabView);
        var x = 0f;
        for (var index = 0; index < tabs.Count; index++)
        {
            var tab = tabs[index];
            var width = tabWidths[index];

            var tabRect = new Rect(x, 0f, width, 30f);
            if (Widgets.ButtonText(tabRect, tab.LabelCap, drawBackground: selectedTab != tab))
            {
                selectedTab = tab;
                selectedProject = null;
                graphScroll = Vector2.zero;
            }

            x += width + 5f;
        }
        Widgets.EndScrollView();

        Text.Font = GameFont.Tiny;
        GUI.color = new Color(1f, 1f, 1f, 0.55f);
        Widgets.Label(new Rect(rect.x, rect.y + 42f, rect.width, 24f), "RR_ShiftHint".Translate());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
    }

    private static void DrawGraph(Rect outerRect)
    {
        Widgets.DrawMenuSection(outerRect);
        var projects = VisibleProjects();
        if (projects.Count == 0)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(outerRect.ContractedBy(20f), "RR_NoProjects".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            return;
        }

        var layout = BuildLayout(projects);
        var maxX = layout.Values.Max(node => node.xMax);
        var maxY = layout.Values.Max(node => node.yMax);
        var viewRect = new Rect(0f, 0f,
            Mathf.Max(outerRect.width - 20f, maxX + 50f),
            Mathf.Max(outerRect.height - 20f, maxY + 50f));

        var scrollRect = outerRect.ContractedBy(4f);
        Widgets.BeginScrollView(scrollRect, ref graphScroll, viewRect);
        DrawConnections(projects, layout);
        foreach (var project in projects)
        {
            DrawNode(project, layout[project]);
        }
        Widgets.EndScrollView();
    }

    private static List<ResearchProjectDef> VisibleProjects()
    {
        var query = DefDatabase<ResearchProjectDef>.AllDefsListForReading
            .Where(project => project.tab == selectedTab);

        if (!search.NullOrEmpty())
        {
            query = query.Where(project =>
                project.LabelCap.ToString().IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                project.defName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        return query.OrderBy(project => project.researchViewX)
            .ThenBy(project => project.researchViewY)
            .ToList();
    }

    private static Dictionary<ResearchProjectDef, Rect> BuildLayout(IEnumerable<ResearchProjectDef> projects)
    {
        var layout = new Dictionary<ResearchProjectDef, Rect>();
        var occupied = new HashSet<(int column, int row)>();

        foreach (var project in projects.OrderBy(item => item.researchViewX)
                     .ThenBy(item => item.researchViewY)
                     .ThenBy(item => item.defName))
        {
            var column = Mathf.Max(0, Mathf.RoundToInt(project.researchViewX));
            var row = Mathf.Max(0, Mathf.RoundToInt(project.researchViewY / 0.7f));
            while (!occupied.Add((column, row)))
            {
                row++;
            }

            layout[project] = new Rect(35f + column * ColumnWidth, 35f + row * RowHeight, NodeWidth, NodeHeight);
        }

        return layout;
    }

    private static void DrawConnections(
        IReadOnlyCollection<ResearchProjectDef> projects,
        IReadOnlyDictionary<ResearchProjectDef, Rect> layout)
    {
        var visible = new HashSet<ResearchProjectDef>(projects);
        foreach (var project in projects)
        {
            if (project.prerequisites == null)
            {
                continue;
            }

            foreach (var prerequisite in project.prerequisites.Where(visible.Contains))
            {
                var from = layout[prerequisite];
                var to = layout[project];
                Widgets.DrawLine(new Vector2(from.xMax, from.center.y), new Vector2(to.xMin, to.center.y), Connection, 2f);
            }
        }
    }

    private static void DrawNode(ResearchProjectDef project, Rect rect)
    {
        var isCurrent = Find.ResearchManager.IsCurrentProject(project);
        var color = project.IsFinished ? Completed : isCurrent ? Current : project.CanStartNow ? Available : Locked;
        Widgets.DrawBoxSolid(rect, color);

        if (selectedProject == project)
        {
            Widgets.DrawBox(rect, 3, TexUI.HighlightTex);
        }
        else
        {
            Widgets.DrawBox(rect, 1);
        }

        Widgets.DrawHighlightIfMouseover(rect);
        if (Widgets.ButtonInvisible(rect))
        {
            selectedProject = project;
            detailsScroll = Vector2.zero;
        }

        var inner = rect.ContractedBy(8f);
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.UpperCenter;
        Widgets.Label(new Rect(inner.x, inner.y, inner.width, 42f), project.LabelCap);

        Text.Font = GameFont.Tiny;
        Text.Anchor = TextAnchor.LowerCenter;
        var status = project.IsFinished
            ? "RR_Completed".Translate()
            : isCurrent
                ? "RR_CurrentResearch".Translate()
                : project.CanStartNow
                    ? "RR_Available".Translate()
                    : "RR_Locked".Translate();
        Widgets.Label(new Rect(inner.x, inner.yMax - 20f, inner.width, 20f), status);
        if (!project.IsFinished && project.ProgressPercent > 0f)
        {
            var progressRect = new Rect(rect.x + 3f, rect.yMax - 5f, rect.width - 6f, 3f);
            Widgets.DrawBoxSolid(progressRect, new Color(0f, 0f, 0f, 0.35f));
            progressRect.width *= Mathf.Clamp01(project.ProgressPercent);
            Widgets.DrawBoxSolid(progressRect, new Color(0.86f, 0.72f, 0.34f));
        }
        Text.Anchor = TextAnchor.UpperLeft;
        Text.Font = GameFont.Small;
    }

    private static void DrawDetails(Rect rect)
    {
        Widgets.DrawMenuSection(rect);
        var inner = rect.ContractedBy(12f);
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(inner.x, inner.y, inner.width, 34f), "RR_Details".Translate());
        Text.Font = GameFont.Small;

        if (selectedProject == null)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.65f);
            Widgets.Label(new Rect(inner.x, inner.y + 50f, inner.width, 80f), "RR_SelectPrompt".Translate());
            GUI.color = Color.white;
            return;
        }

        var project = selectedProject;
        var contentWidth = inner.width - 20f;
        var description = project.description.NullOrEmpty() ? project.LabelCap.ToString() : project.description;
        var descriptionHeight = Text.CalcHeight(description, contentWidth);
        var prerequisiteHeight = Mathf.Max(28f, (project.prerequisites?.Count ?? 0) * 24f + 28f);
        var viewHeight = 170f + descriptionHeight + prerequisiteHeight;
        var scrollOuter = new Rect(inner.x, inner.y + 42f, inner.width, inner.height - 94f);
        var viewRect = new Rect(0f, 0f, contentWidth, viewHeight);

        Widgets.BeginScrollView(scrollOuter, ref detailsScroll, viewRect);
        var y = 0f;
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(0f, y, contentWidth, 38f), project.LabelCap);
        y += 42f;
        Text.Font = GameFont.Small;
        Widgets.Label(new Rect(0f, y, contentWidth, 28f), "RR_Cost".Translate(project.CostApparent.ToString("0")));
        y += 34f;
        Widgets.Label(new Rect(0f, y, contentWidth, descriptionHeight), description);
        y += descriptionHeight + 18f;
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(0f, y, contentWidth, 30f), "RR_Prerequisites".Translate());
        y += 32f;
        Text.Font = GameFont.Small;
        if (project.prerequisites == null || project.prerequisites.Count == 0)
        {
            Widgets.Label(new Rect(0f, y, contentWidth, 24f), "RR_NoPrerequisites".Translate());
        }
        else
        {
            foreach (var prerequisite in project.prerequisites)
            {
                Widgets.Label(new Rect(0f, y, contentWidth, 24f), $"• {prerequisite.LabelCap}");
                y += 24f;
            }
        }
        Widgets.EndScrollView();

        var buttonRect = new Rect(inner.x, rect.yMax - 44f, inner.width, 32f);
        var isCurrent = Find.ResearchManager.IsCurrentProject(project);
        var enabled = !project.IsFinished && project.CanStartNow && !isCurrent;
        if (Widgets.ButtonText(buttonRect, isCurrent ? "RR_CurrentResearch".Translate() : "RR_StartResearch".Translate(), active: enabled))
        {
            Find.ResearchManager.SetCurrentProject(project);
        }
    }
}
