using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ResearchReframed;

internal static class ResearchWindowRenderer
{
    private const float HeaderHeight = 76f;
    private const float DetailsWidth = 350f;
    private const float MinZoom = 0.58f;
    private const float MaxZoom = 1.25f;
    private const float DragThreshold = 6f;

    private static readonly Color Background = new(0.105f, 0.115f, 0.12f);
    private static readonly Color Completed = new(0.19f, 0.29f, 0.22f);
    private static readonly Color Current = new(0.34f, 0.29f, 0.17f);
    private static readonly Color Available = new(0.20f, 0.24f, 0.27f);
    private static readonly Color Locked = new(0.145f, 0.15f, 0.16f);
    private static readonly Color Connection = new(0.43f, 0.44f, 0.42f, 0.82f);
    private static readonly Color HighlightConnection = new(0.86f, 0.69f, 0.29f, 0.95f);
    private static readonly Color Progress = new(0.86f, 0.69f, 0.29f);

    private static Vector2 graphScroll;
    private static Vector2 tabScroll;
    private static Vector2 detailsScroll;
    private static ResearchTabDef? selectedTab;
    private static ResearchProjectDef? selectedProject;
    private static ResearchProjectDef? pendingFocus;
    private static string search = string.Empty;
    private static float zoom = 1f;
    private static Rect lastGraphRect;
    private static Vector2 lastBaseGraphSize;
    private static bool dragCandidate;
    private static bool draggingCanvas;
    private static Vector2 dragStart;

    public static void Draw(Rect rect)
    {
        Widgets.DrawBoxSolid(rect, Background);
        var content = rect.ContractedBy(10f);
        DrawHeader(new Rect(content.x, content.y, content.width, HeaderHeight - 6f));

        var graphRect = new Rect(content.x, content.y + HeaderHeight, content.width, content.height - HeaderHeight);
        var detailsRect = selectedProject == null
            ? Rect.zero
            : new Rect(graphRect.xMax - DetailsWidth - 10f, graphRect.y + 10f,
                DetailsWidth, graphRect.height - 20f);
        DrawGraph(graphRect, detailsRect);

        if (selectedProject != null)
        {
            if (detailsRect.width <= 0f)
            {
                detailsRect = new Rect(graphRect.xMax - DetailsWidth - 10f, graphRect.y + 10f,
                    DetailsWidth, graphRect.height - 20f);
            }
            Widgets.DrawShadowAround(detailsRect);
            DrawDetails(detailsRect);
        }
    }

    private static void DrawHeader(Rect rect)
    {
        var tabs = DefDatabase<ResearchTabDef>.AllDefsListForReading.OrderBy(tab => tab.label).ToList();
        if (selectedTab == null || !tabs.Contains(selectedTab))
        {
            selectedTab = tabs.FirstOrDefault();
        }

        const float controlsWidth = 550f;
        var tabArea = new Rect(rect.x, rect.y, Mathf.Max(180f, rect.width - controlsWidth - 10f), 34f);
        DrawTabs(tabArea, tabs);

        var controlsX = tabArea.xMax + 10f;
        var searchRect = new Rect(controlsX, rect.y, 235f, 30f);
        search = Widgets.TextField(searchRect, search);
        if (search.NullOrEmpty())
        {
            GUI.color = new Color(1f, 1f, 1f, 0.45f);
            Widgets.Label(searchRect.ContractedBy(6f, 3f), "RR_Search".Translate());
            GUI.color = Color.white;
        }

        var x = searchRect.xMax + 6f;
        if (Widgets.ButtonText(new Rect(x, rect.y, 32f, 30f), "−"))
        {
            SetZoom(zoom - 0.1f);
        }
        x += 36f;
        if (Widgets.ButtonText(new Rect(x, rect.y, 58f, 30f), $"{zoom:P0}"))
        {
            SetZoom(1f);
        }
        x += 62f;
        if (Widgets.ButtonText(new Rect(x, rect.y, 32f, 30f), "+"))
        {
            SetZoom(zoom + 0.1f);
        }
        x += 38f;
        if (Widgets.ButtonText(new Rect(x, rect.y, 78f, 30f), "RR_Fit".Translate()))
        {
            FitGraph();
        }
        x += 84f;
        if (Widgets.ButtonText(new Rect(x, rect.y, 105f, 30f), "RR_Current".Translate()))
        {
            FocusCurrentProject();
        }

        Text.Font = GameFont.Tiny;
        GUI.color = new Color(1f, 1f, 1f, 0.58f);
        Widgets.Label(new Rect(rect.x, rect.y + 42f, rect.width, 22f), "RR_NavigationHint".Translate());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
    }

    private static void DrawTabs(Rect rect, IReadOnlyList<ResearchTabDef> tabs)
    {
        var widths = tabs.Select(tab => Mathf.Clamp(Text.CalcSize(tab.LabelCap).x + 24f, 90f, 190f)).ToList();
        var totalWidth = Mathf.Max(rect.width - 16f, widths.Sum() + Mathf.Max(0, tabs.Count - 1) * 5f);
        Widgets.BeginScrollView(rect, ref tabScroll, new Rect(0f, 0f, totalWidth, 30f));
        var x = 0f;
        for (var index = 0; index < tabs.Count; index++)
        {
            var tab = tabs[index];
            var tabRect = new Rect(x, 0f, widths[index], 30f);
            if (Widgets.ButtonText(tabRect, tab.LabelCap, drawBackground: selectedTab != tab))
            {
                selectedTab = tab;
                selectedProject = null;
                graphScroll = Vector2.zero;
                zoom = 1f;
            }
            x += widths[index] + 5f;
        }
        Widgets.EndScrollView();
    }

    private static void DrawGraph(Rect outerRect, Rect detailsRect)
    {
        lastGraphRect = outerRect;
        Widgets.DrawMenuSection(outerRect);
        var projects = ResearchGraphModel.ProjectsFor(selectedTab);
        if (projects.Count == 0)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(outerRect.ContractedBy(20f), "RR_NoProjects".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            return;
        }

        var baseLayout = ResearchGraphLayout.Build(selectedTab, projects, 1f);
        lastBaseGraphSize = GraphSize(baseLayout, outerRect);
        var layout = ResearchGraphLayout.Build(selectedTab, projects, zoom);
        var viewSize = GraphSize(layout, outerRect);
        var viewRect = new Rect(0f, 0f, viewSize.x, viewSize.y);
        var scrollRect = outerRect.ContractedBy(4f);
        var graphInputBlocked = detailsRect.width > 0f && detailsRect.Contains(Event.current.mousePosition);

        HandleGraphInput(scrollRect, viewRect, graphInputBlocked);
        Widgets.BeginScrollView(scrollRect, ref graphScroll, viewRect);
        FocusPendingProject(layout, scrollRect);
        DrawConnections(projects, layout);
        var visibleCanvas = new Rect(
            graphScroll.x - ResearchGraphLayout.NodeWidth,
            graphScroll.y - ResearchGraphLayout.NodeHeight,
            scrollRect.width + ResearchGraphLayout.NodeWidth * 2f,
            scrollRect.height + ResearchGraphLayout.NodeHeight * 2f);
        foreach (var project in projects)
        {
            if (layout[project].Overlaps(visibleCanvas))
            {
                DrawNode(project, layout[project], !graphInputBlocked && !draggingCanvas);
            }
        }
        Widgets.EndScrollView();
    }

    private static Vector2 GraphSize(IReadOnlyDictionary<ResearchProjectDef, Rect> layout, Rect outerRect)
    {
        return new Vector2(
            Mathf.Max(outerRect.width - 20f, layout.Values.Max(node => node.xMax) + 55f),
            Mathf.Max(outerRect.height - 20f, layout.Values.Max(node => node.yMax) + 55f));
    }

    private static void DrawConnections(
        IReadOnlyCollection<ResearchProjectDef> projects,
        IReadOnlyDictionary<ResearchProjectDef, Rect> layout)
    {
        var visible = new HashSet<ResearchProjectDef>(projects);
        var highlighted = ResearchGraphModel.PrerequisitePath(selectedProject);
        var children = projects.ToDictionary(project => project, _ => new List<ResearchProjectDef>());
        foreach (var child in projects)
        {
            foreach (var parent in (child.prerequisites ?? new List<ResearchProjectDef>()).Where(visible.Contains))
            {
                children[parent].Add(child);
            }
        }
        foreach (var childList in children.Values)
        {
            childList.Sort((left, right) => layout[left].center.y.CompareTo(layout[right].center.y));
        }

        foreach (var project in projects)
        {
            if (project.prerequisites == null)
            {
                continue;
            }

            var visiblePrerequisites = project.prerequisites.Where(visible.Contains)
                .OrderBy(prerequisite => layout[prerequisite].center.y)
                .ToList();
            for (var prerequisiteIndex = 0; prerequisiteIndex < visiblePrerequisites.Count; prerequisiteIndex++)
            {
                var prerequisite = visiblePrerequisites[prerequisiteIndex];
                var from = layout[prerequisite];
                var to = layout[project];
                var childIndex = children[prerequisite].IndexOf(project);
                var startY = PortY(from, childIndex, children[prerequisite].Count);
                var endY = PortY(to, prerequisiteIndex, visiblePrerequisites.Count);
                var availableWidth = Mathf.Max(50f, to.xMin - from.xMax);
                var laneOffset = ((childIndex % 5) - 2) * 7f * zoom;
                var middleX = Mathf.Clamp(from.xMax + availableWidth * 0.46f + laneOffset,
                    from.xMax + 20f * zoom, to.xMin - 20f * zoom);
                var start = new Vector2(from.xMax, startY);
                var firstCorner = new Vector2(middleX, startY);
                var secondCorner = new Vector2(middleX, endY);
                var end = new Vector2(to.xMin, endY);
                var isHighlighted = selectedProject == project && highlighted.Contains(prerequisite) ||
                                    highlighted.Contains(project) && highlighted.Contains(prerequisite);
                var color = isHighlighted ? HighlightConnection : Connection;
                var thickness = isHighlighted ? 3f : 2f;
                Widgets.DrawLine(start, firstCorner, color, thickness);
                Widgets.DrawLine(firstCorner, secondCorner, color, thickness);
                Widgets.DrawLine(secondCorner, end, color, thickness);
            }
        }
    }

    private static float PortY(Rect node, int index, int count)
    {
        if (count <= 1)
        {
            return node.center.y;
        }

        var usableHeight = node.height * 0.52f;
        return node.center.y - usableHeight * 0.5f + usableHeight * index / (count - 1);
    }

    private static void DrawNode(ResearchProjectDef project, Rect rect, bool allowInput)
    {
        var isCurrent = Find.ResearchManager.IsCurrentProject(project);
        var matches = ResearchGraphModel.Matches(project, search);
        var color = project.IsFinished ? Completed : isCurrent ? Current : project.CanStartNow ? Available : Locked;
        if (!search.NullOrEmpty() && !matches)
        {
            color *= 0.43f;
            color.a = 1f;
        }

        Widgets.DrawBoxSolid(rect, color);
        var borderThickness = selectedProject == project || matches && !search.NullOrEmpty() ? 3 : 1;
        if (selectedProject == project)
        {
            Widgets.DrawBox(rect, borderThickness, TexUI.HighlightTex);
        }
        else
        {
            Widgets.DrawBox(rect, borderThickness);
        }
        Widgets.DrawHighlightIfMouseover(rect);
        if (allowInput && Widgets.ButtonInvisible(rect))
        {
            selectedProject = project;
            detailsScroll = Vector2.zero;
        }

        var padding = Mathf.Max(5f, 8f * zoom);
        var inner = rect.ContractedBy(padding);
        Text.Font = zoom < 0.72f ? GameFont.Tiny : GameFont.Small;
        Text.Anchor = TextAnchor.UpperCenter;
        Widgets.Label(new Rect(inner.x, inner.y, inner.width, 28f * zoom), project.LabelCap);
        DrawUnlockIcons(project, new Rect(inner.x, inner.y + 30f * zoom, inner.width, 42f * zoom));

        var progressRect = new Rect(inner.x, rect.yMax - 13f * zoom, inner.width, Mathf.Max(5f, 8f * zoom));
        Widgets.DrawBoxSolid(progressRect, new Color(0f, 0f, 0f, 0.5f));
        var filled = progressRect;
        filled.width *= Mathf.Clamp01(project.ProgressPercent);
        Widgets.DrawBoxSolid(filled, project.IsFinished ? new Color(0.38f, 0.66f, 0.42f) : Progress);

        Text.Font = GameFont.Tiny;
        Text.Anchor = TextAnchor.MiddleCenter;
        var status = project.IsFinished
            ? "RR_Completed".Translate()
            : isCurrent
                ? $"{project.ProgressPercent:P0}"
                : project.CanStartNow
                    ? "RR_Available".Translate()
                    : "RR_Locked".Translate();
        if (zoom >= 0.72f)
        {
            var statusHeight = Mathf.Max(Text.LineHeight + 2f, 22f);
            Widgets.Label(new Rect(inner.x, progressRect.y - statusHeight - 2f, inner.width, statusHeight), status);
        }
        if (allowInput)
        {
            TooltipHandler.TipRegion(rect, NodeTooltip(project));
        }
        Text.Anchor = TextAnchor.UpperLeft;
        Text.Font = GameFont.Small;
    }

    private static void DrawUnlockIcons(ResearchProjectDef project, Rect area)
    {
        var unlocks = ResearchGraphModel.UnlocksFor(project);
        var withIcons = unlocks.Where(unlock => unlock.Icon != null).Take(4).ToList();
        if (withIcons.Count == 0)
        {
            return;
        }

        var iconSize = Mathf.Min(area.height, 38f * zoom);
        var spacing = 4f * zoom;
        var totalWidth = withIcons.Count * iconSize + (withIcons.Count - 1) * spacing;
        var x = area.center.x - totalWidth * 0.5f;
        foreach (var unlock in withIcons)
        {
            var iconRect = new Rect(x, area.y, iconSize, iconSize);
            GUI.color = Color.white;
            Widgets.DrawTextureFitted(iconRect, unlock.Icon!, 1f);
            TooltipHandler.TipRegion(iconRect, unlock.Label);
            x += iconSize + spacing;
        }

        if (unlocks.Count > withIcons.Count)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerRight;
            Widgets.Label(area, $"+{unlocks.Count - withIcons.Count}");
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }

    private static string NodeTooltip(ResearchProjectDef project)
    {
        var unlocks = ResearchGraphModel.UnlocksFor(project);
        var unlockText = unlocks.Count == 0
            ? "RR_NoUnlocks".Translate().ToString()
            : string.Join("\n", unlocks.Select(unlock => $"• {unlock.Label}"));
        var progressText = "RR_Progress".Translate(project.ProgressPercent.ToString("P0"));
        var unlockHeading = "RR_Unlocks".Translate();
        return $"{project.LabelCap}\n{progressText}\n\n{unlockHeading}\n{unlockText}";
    }

    private static void DrawDetails(Rect rect)
    {
        Widgets.DrawMenuSection(rect);
        var inner = rect.ContractedBy(12f);
        if (Widgets.ButtonText(new Rect(inner.xMax - 28f, inner.y, 28f, 28f), "×"))
        {
            selectedProject = null;
            return;
        }

        var project = selectedProject!;
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(inner.x, inner.y, inner.width - 34f, 34f), project.LabelCap);
        Text.Font = GameFont.Small;

        var contentWidth = inner.width - 20f;
        var description = project.description.NullOrEmpty() ? project.LabelCap.ToString() : project.description;
        var descriptionHeight = Text.CalcHeight(description, contentWidth);
        var prerequisites = project.prerequisites ?? new List<ResearchProjectDef>();
        var unlocks = ResearchGraphModel.UnlocksFor(project);
        var viewHeight = 255f + descriptionHeight + prerequisites.Count * 25f + unlocks.Count * 25f;
        var scrollOuter = new Rect(inner.x, inner.y + 42f, inner.width, inner.height - 94f);
        Widgets.BeginScrollView(scrollOuter, ref detailsScroll, new Rect(0f, 0f, contentWidth, viewHeight));

        var y = 0f;
        Widgets.Label(new Rect(0f, y, contentWidth, 28f), "RR_Cost".Translate(project.CostApparent.ToString("0")));
        y += 32f;
        DrawDetailedProgress(project, new Rect(0f, y, contentWidth, 30f));
        y += 44f;
        Widgets.Label(new Rect(0f, y, contentWidth, descriptionHeight), description);
        y += descriptionHeight + 18f;
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(0f, y, contentWidth, 30f), "RR_Unlocks".Translate());
        y += 32f;
        Text.Font = GameFont.Small;
        if (unlocks.Count == 0)
        {
            Widgets.Label(new Rect(0f, y, contentWidth, 24f), "RR_NoUnlocks".Translate());
            y += 25f;
        }
        else
        {
            foreach (var unlock in unlocks)
            {
                if (unlock.Icon != null)
                {
                    Widgets.DrawTextureFitted(new Rect(0f, y, 22f, 22f), unlock.Icon, 1f);
                }
                Widgets.Label(new Rect(28f, y, contentWidth - 28f, 24f), unlock.Label);
                y += 25f;
            }
        }

        y += 10f;
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(0f, y, contentWidth, 30f), "RR_Prerequisites".Translate());
        y += 32f;
        Text.Font = GameFont.Small;
        if (prerequisites.Count == 0)
        {
            Widgets.Label(new Rect(0f, y, contentWidth, 24f), "RR_NoPrerequisites".Translate());
        }
        else
        {
            foreach (var prerequisite in prerequisites)
            {
                if (Widgets.ButtonText(new Rect(0f, y, contentWidth, 24f), prerequisite.LabelCap))
                {
                    selectedTab = prerequisite.tab;
                    pendingFocus = prerequisite;
                }
                y += 25f;
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

    private static void DrawDetailedProgress(ResearchProjectDef project, Rect rect)
    {
        Widgets.DrawBoxSolid(rect, new Color(0.06f, 0.065f, 0.07f, 0.95f));
        var fillRect = rect.ContractedBy(2f);
        fillRect.width *= Mathf.Clamp01(project.ProgressPercent);
        Widgets.DrawBoxSolid(fillRect,
            project.IsFinished ? new Color(0.35f, 0.62f, 0.40f) : Progress);
        Widgets.DrawBox(rect, 1);

        var progress = Mathf.Min(project.ProgressApparent, project.CostApparent);
        var pointText = $"{progress:0} / {project.CostApparent:0} ({project.ProgressPercent:P0})";
        var completedText = "RR_Completed".Translate();
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(rect, project.IsFinished ? $"{completedText} · {pointText}" : pointText);
        Text.Anchor = TextAnchor.UpperLeft;
    }

    private static void HandleGraphInput(Rect graphRect, Rect viewRect, bool inputBlocked)
    {
        var currentEvent = Event.current;
        if (currentEvent.type == EventType.MouseUp && currentEvent.button == 0 && dragCandidate)
        {
            if (draggingCanvas)
            {
                currentEvent.Use();
            }
            dragCandidate = false;
            draggingCanvas = false;
            return;
        }

        if (inputBlocked)
        {
            dragCandidate = false;
            draggingCanvas = false;
            return;
        }

        var pointerInside = graphRect.Contains(currentEvent.mousePosition);
        if (pointerInside && currentEvent.type == EventType.ScrollWheel && currentEvent.control)
        {
            SetZoom(zoom - currentEvent.delta.y * 0.04f);
            currentEvent.Use();
        }
        else if (pointerInside && currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
        {
            dragCandidate = true;
            draggingCanvas = false;
            dragStart = currentEvent.mousePosition;
        }
        else if (dragCandidate && currentEvent.type == EventType.MouseDrag && currentEvent.button == 0)
        {
            if (!draggingCanvas && Vector2.Distance(dragStart, currentEvent.mousePosition) >= DragThreshold)
            {
                draggingCanvas = true;
            }

            if (draggingCanvas)
            {
                graphScroll -= currentEvent.delta;
                graphScroll.x = Mathf.Clamp(graphScroll.x, 0f, Mathf.Max(0f, viewRect.width - graphRect.width));
                graphScroll.y = Mathf.Clamp(graphScroll.y, 0f, Mathf.Max(0f, viewRect.height - graphRect.height));
                currentEvent.Use();
            }
        }
    }

    private static void SetZoom(float value)
    {
        var previous = zoom;
        zoom = Mathf.Clamp(value, MinZoom, MaxZoom);
        if (Math.Abs(previous) > 0.001f)
        {
            graphScroll *= zoom / previous;
        }
    }

    private static void FitGraph()
    {
        if (lastGraphRect.width <= 0f || lastBaseGraphSize.x <= 0f)
        {
            return;
        }

        var horizontal = (lastGraphRect.width - 30f) / lastBaseGraphSize.x;
        var vertical = (lastGraphRect.height - 30f) / lastBaseGraphSize.y;
        zoom = Mathf.Clamp(Mathf.Min(horizontal, vertical), MinZoom, 1f);
        graphScroll = Vector2.zero;
    }

    private static void FocusCurrentProject()
    {
        var current = Find.ResearchManager.GetProject();
        if (current == null)
        {
            Messages.Message("RR_NoCurrentResearch".Translate(), MessageTypeDefOf.NeutralEvent, false);
            return;
        }

        selectedTab = current.tab;
        selectedProject = current;
        pendingFocus = current;
    }

    private static void FocusPendingProject(IReadOnlyDictionary<ResearchProjectDef, Rect> layout, Rect viewport)
    {
        if (pendingFocus == null || !layout.TryGetValue(pendingFocus, out var target))
        {
            return;
        }

        selectedProject = pendingFocus;
        graphScroll = new Vector2(
            Mathf.Max(0f, target.center.x - viewport.width * 0.5f),
            Mathf.Max(0f, target.center.y - viewport.height * 0.5f));
        pendingFocus = null;
    }
}
