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
    private static readonly Color DependentConnection = new(0.42f, 0.67f, 0.78f, 0.95f);
    private static readonly Color SelectedBorder = new(0.95f, 0.73f, 0.22f);
    private static readonly Color Progress = new(0.86f, 0.69f, 0.29f);

    private static Vector2 graphScroll;
    private static Vector2 tabScroll;
    private static Vector2 detailsScroll;
    private static ResearchTabDef? selectedTab;
    private static ResearchProjectDef? selectedProject;
    private static ResearchProjectDef? pendingFocus;
    private static ResearchProjectDef? pendingEnsureVisible;
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
        var baseRoutes = ResearchEdgeRouter.Build(selectedTab, projects, baseLayout);
        lastBaseGraphSize = GraphSize(baseLayout, outerRect, 55f);
        var layout = ResearchGraphLayout.Build(selectedTab, projects, zoom);
        var rightSafePadding = detailsRect.width > 0f ? detailsRect.width + 90f : 55f;
        var viewSize = GraphSize(layout, outerRect, rightSafePadding);
        var viewRect = new Rect(0f, 0f, viewSize.x, viewSize.y);
        var scrollRect = outerRect.ContractedBy(4f);
        var graphInputBlocked = detailsRect.width > 0f && detailsRect.Contains(Event.current.mousePosition);

        HandleGraphInput(scrollRect, viewRect, graphInputBlocked);
        Widgets.BeginScrollView(scrollRect, ref graphScroll, viewRect);
        FocusPendingProject(layout, scrollRect, viewRect, detailsRect.width);
        EnsureSelectedVisible(layout, scrollRect, viewRect, detailsRect.width);
        DrawConnections(baseRoutes, zoom);
        var prerequisites = ResearchGraphModel.PrerequisitePath(selectedProject);
        var dependents = ResearchGraphModel.DependentPath(selectedProject);
        var visibleCanvas = new Rect(
            graphScroll.x - ResearchGraphLayout.NodeWidth,
            graphScroll.y - ResearchGraphLayout.NodeHeight,
            scrollRect.width + ResearchGraphLayout.NodeWidth * 2f,
            scrollRect.height + ResearchGraphLayout.NodeHeight * 2f);
        foreach (var project in projects)
        {
            if (layout[project].Overlaps(visibleCanvas))
            {
                DrawNode(project, layout[project], !graphInputBlocked && !draggingCanvas,
                    prerequisites, dependents);
            }
        }
        Widgets.EndScrollView();
    }

    private static Vector2 GraphSize(
        IReadOnlyDictionary<ResearchProjectDef, Rect> layout,
        Rect outerRect,
        float rightPadding)
    {
        return new Vector2(
            Mathf.Max(outerRect.width - 20f, layout.Values.Max(node => node.xMax) + rightPadding),
            Mathf.Max(outerRect.height - 20f, layout.Values.Max(node => node.yMax) + 55f));
    }

    private static void DrawConnections(
        IReadOnlyList<ResearchEdgeRoute> routes,
        float scale)
    {
        var prerequisites = ResearchGraphModel.PrerequisitePath(selectedProject);
        var dependents = ResearchGraphModel.DependentPath(selectedProject);
        foreach (var route in routes)
        {
            var prerequisitePath = selectedProject != null && prerequisites.Contains(route.From) &&
                                   (route.To == selectedProject || prerequisites.Contains(route.To));
            var dependentPath = selectedProject != null &&
                                (route.From == selectedProject || dependents.Contains(route.From)) &&
                                dependents.Contains(route.To);
            var color = prerequisitePath
                ? HighlightConnection
                : dependentPath
                    ? DependentConnection
                    : selectedProject == null
                        ? Connection
                        : new Color(Connection.r, Connection.g, Connection.b, 0.20f);
            var thickness = prerequisitePath || dependentPath ? 3.5f : 2f;
            var points = route.Points.Select(point => point * scale).ToList();
            DrawOrthogonalPath(points, color, thickness);
            DrawArrow(points, color, thickness);
        }
    }

    private static void DrawOrthogonalPath(IReadOnlyList<Vector2> points, Color color, float thickness)
    {
        var snappedThickness = Mathf.Max(1f, Mathf.Round(thickness));
        var half = snappedThickness * 0.5f;
        for (var index = 1; index < points.Count; index++)
        {
            var from = new Vector2(Mathf.Round(points[index - 1].x) + 0.5f, Mathf.Round(points[index - 1].y) + 0.5f);
            var to = new Vector2(Mathf.Round(points[index].x) + 0.5f, Mathf.Round(points[index].y) + 0.5f);
            Rect segment;
            if (Mathf.Abs(from.y - to.y) < 0.1f)
            {
                segment = new Rect(Mathf.Min(from.x, to.x) - half, from.y - half,
                    Mathf.Abs(to.x - from.x) + snappedThickness, snappedThickness);
            }
            else
            {
                segment = new Rect(from.x - half, Mathf.Min(from.y, to.y) - half,
                    snappedThickness, Mathf.Abs(to.y - from.y) + snappedThickness);
            }
            Widgets.DrawBoxSolid(segment, color);
        }

        for (var index = 1; index < points.Count - 1; index++)
        {
            var joint = points[index];
            Widgets.DrawBoxSolid(new Rect(
                Mathf.Round(joint.x) + 0.5f - half,
                Mathf.Round(joint.y) + 0.5f - half,
                snappedThickness,
                snappedThickness), color);
        }
    }

    private static void DrawArrow(IReadOnlyList<Vector2> points, Color color, float thickness)
    {
        if (points.Count < 2)
        {
            return;
        }

        var end = points[points.Count - 1];
        var size = Mathf.Max(5f, 6f * zoom);
        Widgets.DrawLine(end, new Vector2(end.x - size, end.y - size * 0.6f), color, thickness);
        Widgets.DrawLine(end, new Vector2(end.x - size, end.y + size * 0.6f), color, thickness);
    }

    private static void DrawNode(
        ResearchProjectDef project,
        Rect rect,
        bool allowInput,
        ISet<ResearchProjectDef> prerequisites,
        ISet<ResearchProjectDef> dependents)
    {
        var isCurrent = Find.ResearchManager.IsCurrentProject(project);
        var matches = ResearchGraphModel.Matches(project, search);
        var color = project.IsFinished ? Completed : isCurrent ? Current : project.CanStartNow ? Available : Locked;
        var relatedToSelection = selectedProject == null || selectedProject == project ||
                                 prerequisites.Contains(project) || dependents.Contains(project);
        if (!relatedToSelection)
        {
            color *= 0.68f;
            color.a = 1f;
        }
        if (!search.NullOrEmpty() && !matches)
        {
            color *= 0.43f;
            color.a = 1f;
        }

        if (selectedProject == project)
        {
            Widgets.DrawShadowAround(rect.ExpandedBy(5f));
            Widgets.DrawBoxSolid(rect.ExpandedBy(4f), SelectedBorder);
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
            pendingEnsureVisible = project;
            detailsScroll = Vector2.zero;
        }

        if (selectedProject == project)
        {
            var markerWidth = Mathf.Min(rect.width * 0.42f, 92f * zoom);
            Widgets.DrawBoxSolid(new Rect(rect.center.x - markerWidth * 0.5f, rect.y - 6f,
                markerWidth, 5f), SelectedBorder);
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
        var researchFacilities = project.requiredResearchFacilities ?? new List<ThingDef>();
        var facilityCount = researchFacilities.Count;
        var missingResearchBench = !project.IsFinished && !project.PlayerHasAnyAppropriateResearchBench;
        var requirementRows = (project.requiredResearchBuilding != null ? 1 : 0) + facilityCount;
        var requirementLabels = (project.requiredResearchBuilding != null ? 1 : 0) + (facilityCount > 0 ? 1 : 0);
        var requirementHeight = requirementRows == 0
            ? 0f
            : 52f + requirementRows * 29f + requirementLabels * 25f +
              (missingResearchBench ? 30f : 0f);
        var viewHeight = 265f + descriptionHeight + requirementHeight +
                         prerequisites.Count * 25f + unlocks.Count * 29f;
        var scrollOuter = new Rect(inner.x, inner.y + 42f, inner.width, inner.height - 94f);
        Widgets.BeginScrollView(scrollOuter, ref detailsScroll, new Rect(0f, 0f, contentWidth, viewHeight));

        var y = 0f;
        Widgets.Label(new Rect(0f, y, contentWidth, 28f), "RR_Cost".Translate(project.CostApparent.ToString("0")));
        y += 32f;
        DrawDetailedProgress(project, new Rect(0f, y, contentWidth, 30f));
        y += 44f;
        Widgets.Label(new Rect(0f, y, contentWidth, descriptionHeight), description);
        y += descriptionHeight + 18f;

        if (requirementRows > 0)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, contentWidth, 30f), "RR_ResearchRequirements".Translate());
            y += 32f;
            Text.Font = GameFont.Small;
            if (project.requiredResearchBuilding != null)
            {
                Widgets.Label(new Rect(0f, y, contentWidth, 24f), "RR_RequiredBench".Translate());
                y += 25f;
                DrawDefRow(project.requiredResearchBuilding, ref y, contentWidth);
            }

            if (facilityCount > 0)
            {
                Widgets.Label(new Rect(0f, y, contentWidth, 24f), "RR_RequiredFacilities".Translate());
                y += 25f;
                foreach (var facility in researchFacilities)
                {
                    DrawDefRow(facility, ref y, contentWidth);
                }
            }

            if (missingResearchBench)
            {
                GUI.color = new Color(1f, 0.66f, 0.28f);
                Widgets.Label(new Rect(0f, y, contentWidth, 28f), "RR_MissingResearchBench".Translate());
                GUI.color = Color.white;
                y += 30f;
            }
            y += 10f;
        }

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
                DrawDefRow(unlock.Definition, ref y, contentWidth, unlock.Label, unlock.Icon);
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

    private static void DrawDefRow(
        Def definition,
        ref float y,
        float contentWidth,
        string? label = null,
        Texture2D? icon = null)
    {
        var row = new Rect(0f, y, contentWidth, 27f);
        Widgets.DrawHighlightIfMouseover(row);
        var infoRect = new Rect(row.xMax - Widgets.InfoCardButtonSize, row.y + 1f,
            Widgets.InfoCardButtonSize, Widgets.InfoCardButtonSize);
        var clickRect = new Rect(row.x, row.y, row.width - Widgets.InfoCardButtonSize - 4f, row.height);
        if (Widgets.ButtonInvisible(clickRect))
        {
            Find.WindowStack.Add(new Dialog_InfoCard(definition, null));
        }

        var resolvedIcon = icon ?? (definition as ThingDef)?.uiIcon ?? (definition as BuildableDef)?.uiIcon;
        if (resolvedIcon != null)
        {
            Widgets.DrawTextureFitted(new Rect(row.x + 1f, row.y + 1f, 24f, 24f), resolvedIcon, 1f);
        }
        Widgets.Label(new Rect(row.x + 31f, row.y + 2f, row.width - 64f, 24f),
            label ?? definition.LabelCap.ToString());
        Widgets.InfoCardButton(infoRect, definition);
        y += 29f;
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

        var canvasRect = graphRect;
        if (viewRect.width > graphRect.width)
        {
            canvasRect.height -= GenUI.ScrollBarWidth;
        }
        if (viewRect.height > graphRect.height)
        {
            canvasRect.width -= GenUI.ScrollBarWidth;
        }

        var pointerInside = canvasRect.Contains(currentEvent.mousePosition);
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

    private static void FocusPendingProject(
        IReadOnlyDictionary<ResearchProjectDef, Rect> layout,
        Rect viewport,
        Rect viewRect,
        float obscuredWidth)
    {
        if (pendingFocus == null || !layout.TryGetValue(pendingFocus, out var target))
        {
            return;
        }

        selectedProject = pendingFocus;
        var availableWidth = Mathf.Max(220f, viewport.width - obscuredWidth - 34f);
        graphScroll = new Vector2(
            Mathf.Clamp(target.center.x - availableWidth * 0.5f,
                0f, Mathf.Max(0f, viewRect.width - viewport.width)),
            Mathf.Clamp(target.center.y - viewport.height * 0.5f,
                0f, Mathf.Max(0f, viewRect.height - viewport.height)));
        pendingFocus = null;
    }

    private static void EnsureSelectedVisible(
        IReadOnlyDictionary<ResearchProjectDef, Rect> layout,
        Rect viewport,
        Rect viewRect,
        float obscuredWidth)
    {
        if (pendingEnsureVisible == null || !layout.TryGetValue(pendingEnsureVisible, out var target))
        {
            return;
        }

        const float margin = 32f;
        var availableWidth = Mathf.Max(220f, viewport.width - obscuredWidth - margin);
        var visibleLeft = graphScroll.x + margin;
        var visibleRight = graphScroll.x + availableWidth;
        if (target.xMax > visibleRight)
        {
            graphScroll.x += target.xMax - visibleRight;
        }
        else if (target.xMin < visibleLeft)
        {
            graphScroll.x -= visibleLeft - target.xMin;
        }

        graphScroll.x = Mathf.Clamp(graphScroll.x, 0f, Mathf.Max(0f, viewRect.width - viewport.width));
        graphScroll.y = Mathf.Clamp(graphScroll.y, 0f, Mathf.Max(0f, viewRect.height - viewport.height));
        pendingEnsureVisible = null;
    }
}
