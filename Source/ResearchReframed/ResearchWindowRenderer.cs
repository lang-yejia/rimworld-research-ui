using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ResearchReframed;

internal static class ResearchWindowRenderer
{
    private const float HeaderHeight = 112f;
    private const float DetailsWidth = 350f;
    private const float MinZoom = 0.58f;
    private const float MaxZoom = 1.25f;
    private const float DragThreshold = 6f;

    // Frontier Atlas: matte, warm industrial surfaces with bright, unambiguous status accents.
    private static readonly Color Background = new(0.075f, 0.082f, 0.084f);
    private static readonly Color GraphSurface = new(0.095f, 0.102f, 0.101f);
    private static readonly Color HeaderSurface = new(0.135f, 0.14f, 0.13f);
    private static readonly Color PanelSurface = new(0.155f, 0.16f, 0.148f);
    private static readonly Color NodeSurface = new(0.185f, 0.19f, 0.175f);
    private static readonly Color NodeSurfaceHover = new(0.215f, 0.22f, 0.202f);
    private static readonly Color TrackSurface = new(0.055f, 0.06f, 0.058f, 0.92f);
    private static readonly Color Border = new(0.39f, 0.385f, 0.35f, 0.72f);
    private static readonly Color MutedText = new(0.73f, 0.70f, 0.64f);
    private static readonly Color Completed = new(0.38f, 0.76f, 0.49f);
    private static readonly Color Current = new(0.96f, 0.64f, 0.25f);
    private static readonly Color Available = new(0.35f, 0.79f, 0.83f);
    private static readonly Color Locked = new(0.50f, 0.50f, 0.47f);
    private static readonly Color Connection = new(0.44f, 0.44f, 0.40f, 0.72f);
    private static readonly Color HighlightConnection = new(0.96f, 0.69f, 0.30f, 0.98f);
    private static readonly Color DependentConnection = new(0.35f, 0.79f, 0.83f, 0.96f);
    private static readonly Color SelectedBorder = new(0.97f, 0.71f, 0.31f);
    private static readonly Color SearchBorder = new(0.32f, 0.86f, 0.91f);
    private static readonly Color Progress = new(0.35f, 0.79f, 0.83f);
    private static readonly Color SelectedTab = new(0.245f, 0.235f, 0.19f);
    private static readonly Color Danger = new(0.94f, 0.28f, 0.25f);
    private static readonly Color TechprintAccent = new(0.94f, 0.73f, 0.27f);
    private static readonly Color MechanitorAccent = new(0.69f, 0.48f, 0.91f);
    private static readonly Color AnalysisAccent = new(0.31f, 0.78f, 0.88f);

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
    private static float detailsScrollTarget;
    private static float detailsBounce;
    private static float detailsBounceVelocity;
    private static float pendingDetailsWheel;
    private static Rect lastSearchRect;
    private static float searchDropdownTop;
    private static bool searchFieldFocused;
    private static bool searchDropdownOpen;
    private static readonly Dictionary<ResearchTabDef, TabViewState> TabStates = new();

    public static void Draw(Rect rect)
    {
        if (selectedProject != null &&
            (selectedProject.IsHidden || !ResearchGraphModel.IsVisible(selectedProject) ||
             !Find.ResearchManager.TabInfoVisible(selectedProject.tab)))
        {
            selectedProject = null;
        }

        Widgets.DrawBoxSolid(rect, Background);
        var content = rect.ContractedBy(10f);
        DrawHeader(new Rect(content.x, content.y, content.width, HeaderHeight - 6f));

        var graphRect = new Rect(content.x, content.y + HeaderHeight, content.width, content.height - HeaderHeight);
        var detailsRect = selectedProject == null
            ? Rect.zero
            : new Rect(graphRect.xMax - DetailsWidth - 10f, graphRect.y + 10f,
                DetailsWidth, graphRect.height - 20f);
        var searchResults = BuildSearchResults();
        var searchResultsRect = SearchResultsRect(searchResults.Count);
        if (searchResultsRect.width > 0f && Event.current.type == EventType.MouseDown &&
            Event.current.button == 0 && !searchResultsRect.Contains(Event.current.mousePosition) &&
            !lastSearchRect.Contains(Event.current.mousePosition))
        {
            searchDropdownOpen = false;
            searchResults.Clear();
            searchResultsRect = Rect.zero;
        }
        CaptureDetailsWheel(detailsRect);
        if (searchResultsRect.width > 0f && searchResultsRect.Contains(Event.current.mousePosition) &&
            Event.current.type == EventType.ScrollWheel)
        {
            Event.current.Use();
        }
        DrawGraph(graphRect, detailsRect, searchResultsRect);

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

        DrawSearchResults(searchResults, searchResultsRect);
    }

    private static void DrawHeader(Rect rect)
    {
        DrawPanel(rect, HeaderSurface, Border);
        Widgets.DrawBoxSolid(new Rect(rect.x + 1f, rect.yMax - 2f, rect.width - 2f, 2f),
            new Color(SelectedBorder.r, SelectedBorder.g, SelectedBorder.b, 0.55f));
        var inner = rect.ContractedBy(8f);
        var tabs = DefDatabase<ResearchTabDef>.AllDefsListForReading
            .Where(tab => Find.ResearchManager.TabInfoVisible(tab))
            .OrderBy(tab => tab.label)
            .ToList();
        if (selectedTab == null || !tabs.Contains(selectedTab))
        {
            SwitchTab(tabs.FirstOrDefault());
        }

        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.MiddleLeft;
        GUI.color = MutedText;
        Widgets.Label(new Rect(inner.x, inner.y, 82f, 30f), "RR_Categories".Translate());
        GUI.color = Color.white;
        Text.Anchor = TextAnchor.UpperLeft;

        var tabArea = new Rect(inner.x + 86f, inner.y, inner.width - 86f, 34f);
        DrawTabs(tabArea, tabs);

        Widgets.DrawBoxSolid(new Rect(inner.x, inner.y + 39f, inner.width, 1f), Border);
        var controlsY = inner.y + 47f;
        const float actionsWidth = 323f;
        var actionsX = inner.xMax - actionsWidth;
        var searchWidth = Mathf.Clamp(actionsX - inner.x - 10f, 180f, 420f);
        var searchRect = new Rect(inner.x, controlsY, searchWidth, 30f);
        lastSearchRect = searchRect;
        searchDropdownTop = rect.yMax + 4f;
        var textRect = new Rect(searchRect.x, searchRect.y, searchRect.width - 31f, searchRect.height);
        var previousSearch = search;
        GUI.SetNextControlName("ResearchReframedSearch");
        search = Widgets.TextField(textRect, search);
        searchFieldFocused = GUI.GetNameOfFocusedControl() == "ResearchReframedSearch";
        if (search != previousSearch)
        {
            search = search.Replace("\u200B", string.Empty).Replace("\uFEFF", string.Empty);
            searchDropdownOpen = !ResearchGraphModel.NormalizeQuery(search).NullOrEmpty();
        }
        else if (searchFieldFocused && !ResearchGraphModel.NormalizeQuery(search).NullOrEmpty())
        {
            searchDropdownOpen = true;
        }
        if (search.NullOrEmpty())
        {
            GUI.color = new Color(1f, 1f, 1f, 0.45f);
            Widgets.Label(textRect.ContractedBy(6f, 3f), "RR_Search".Translate());
            GUI.color = Color.white;
        }
        if (!search.NullOrEmpty() && Widgets.ButtonText(new Rect(textRect.xMax + 2f, controlsY, 29f, 30f), "×"))
        {
            search = string.Empty;
            searchDropdownOpen = false;
            GUI.FocusControl(null);
        }

        var x = actionsX;
        if (DrawToolbarButton(new Rect(x, controlsY, 32f, 30f), "−"))
        {
            SetZoom(zoom - 0.1f);
        }
        x += 36f;
        if (DrawToolbarButton(new Rect(x, controlsY, 58f, 30f), $"{zoom:P0}"))
        {
            SetZoom(1f);
        }
        x += 62f;
        if (DrawToolbarButton(new Rect(x, controlsY, 32f, 30f), "+"))
        {
            SetZoom(zoom + 0.1f);
        }
        x += 38f;
        if (DrawToolbarButton(new Rect(x, controlsY, 78f, 30f), "RR_Fit".Translate().ToString()))
        {
            FitGraph();
        }
        x += 84f;
        if (DrawToolbarButton(new Rect(x, controlsY, 101f, 30f), "RR_Current".Translate().ToString(), true))
        {
            FocusCurrentProject();
        }

        Text.Font = GameFont.Tiny;
        var hintRect = new Rect(inner.x, inner.y + 81f, inner.width * 0.57f, 18f);
        DrawHeaderContext(hintRect);
        DrawStatusLegend(new Rect(hintRect.xMax, hintRect.y, inner.xMax - hintRect.xMax, hintRect.height));
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
            var isSelected = selectedTab == tab;
            if (isSelected)
            {
                Widgets.DrawBoxSolid(tabRect, SelectedTab);
                DrawBorder(tabRect, Border, 1);
                Widgets.DrawBoxSolid(new Rect(tabRect.x + 6f, tabRect.yMax - 3f, tabRect.width - 12f, 3f), SelectedBorder);
            }
            else if (Mouse.IsOver(tabRect))
            {
                Widgets.DrawBoxSolid(tabRect, NodeSurfaceHover);
            }

            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = isSelected ? Color.white : MutedText;
            Widgets.Label(tabRect, tab.LabelCap);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            var clicked = Widgets.ButtonInvisible(tabRect);

            if (clicked && selectedTab != tab)
            {
                SwitchTab(tab);
            }
            x += widths[index] + 5f;
        }
        Widgets.EndScrollView();
    }

    private static List<SearchResult> BuildSearchResults()
    {
        var query = ResearchGraphModel.NormalizeQuery(search);
        if (!searchDropdownOpen || query.NullOrEmpty())
        {
            return new List<SearchResult>();
        }

        var results = new List<SearchResult>();
        foreach (var project in DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(project =>
                     project.tab != null && !project.IsHidden && ResearchGraphModel.IsVisible(project) &&
                     Find.ResearchManager.TabInfoVisible(project.tab)))
        {
            var score = MatchScore(project.LabelCap.ToString(), query, 0);
            score = Mathf.Min(score, MatchScore(project.defName, query, 4));
            string? matchedUnlock = null;
            foreach (var unlock in ResearchGraphModel.UnlocksFor(project))
            {
                var unlockScore = MatchScore(unlock.Label, query, 30);
                unlockScore = Mathf.Min(unlockScore,
                    MatchScore(unlock.DisplayDefinition.LabelCap.ToString(), query, 30));
                if (unlockScore < score)
                {
                    score = unlockScore;
                    matchedUnlock = unlock.Label;
                }
            }

            if (project.customUnlockTexts != null)
            {
                foreach (var customUnlock in project.customUnlockTexts)
                {
                    var unlockScore = MatchScore(customUnlock, query, 30);
                    if (unlockScore < score)
                    {
                        score = unlockScore;
                        matchedUnlock = customUnlock;
                    }
                }
            }

            if (score < 1000)
            {
                results.Add(new SearchResult(project, matchedUnlock, score));
            }
        }

        return results.OrderBy(result => result.Score)
            .ThenBy(result => result.Project.LabelCap.ToString())
            .Take(8)
            .ToList();
    }

    private static int MatchScore(string candidate, string query, int baseScore)
    {
        var normalizedCandidate = ResearchGraphModel.NormalizeQuery(candidate);
        if (normalizedCandidate.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return baseScore;
        }
        if (normalizedCandidate.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return baseScore + 8;
        }
        return normalizedCandidate.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
            ? baseScore + 16
            : 1000;
    }

    private static Rect SearchResultsRect(int resultCount)
    {
        var query = ResearchGraphModel.NormalizeQuery(search);
        if (!searchDropdownOpen || query.NullOrEmpty() || lastSearchRect.width <= 0f)
        {
            return Rect.zero;
        }

        var rows = Mathf.Max(1, resultCount);
        return new Rect(lastSearchRect.x, searchDropdownTop,
            Mathf.Max(380f, lastSearchRect.width), rows * 44f + 10f);
    }

    private static void DrawSearchResults(IReadOnlyList<SearchResult> results, Rect rect)
    {
        if (rect.width <= 0f)
        {
            return;
        }

        Widgets.DrawShadowAround(rect);
        DrawPanel(rect, PanelSurface, SearchBorder);
        if (results.Count == 0)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(1f, 1f, 1f, 0.68f);
            Widgets.Label(rect.ContractedBy(8f), "RR_NoSearchResults".Translate());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            return;
        }

        var y = rect.y + 5f;
        foreach (var result in results)
        {
            var row = new Rect(rect.x + 5f, y, rect.width - 10f, 42f);
            if (Mouse.IsOver(row))
            {
                Widgets.DrawBoxSolid(row, NodeSurfaceHover);
                Widgets.DrawBoxSolid(new Rect(row.x, row.y + 3f, 3f, row.height - 6f), SearchBorder);
            }
            if (Widgets.ButtonInvisible(row))
            {
                SwitchTab(result.Project.tab);
                selectedProject = result.Project;
                pendingFocus = result.Project;
                pendingEnsureVisible = result.Project;
                ResetDetailsScroll();
                GUI.FocusControl(null);
                searchFieldFocused = false;
                searchDropdownOpen = false;
            }

            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(row.x + 8f, row.y + 2f, row.width - 16f, 23f), result.Project.LabelCap);
            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            var subtitle = result.Project.tab.LabelCap.ToString();
            if (!result.MatchedUnlock.NullOrEmpty())
            {
                var unlockMatch = "RR_SearchUnlockMatch".Translate(result.MatchedUnlock);
                subtitle += $" · {unlockMatch}";
            }
            Widgets.Label(new Rect(row.x + 8f, row.y + 23f, row.width - 16f, 17f), subtitle);
            GUI.color = Color.white;
            y += 44f;
        }
        Text.Font = GameFont.Small;
    }

    private static void DrawGraph(Rect outerRect, Rect detailsRect, Rect searchResultsRect)
    {
        lastGraphRect = outerRect;
        DrawGraphBackdrop(outerRect);
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
        var graphInputBlocked = detailsRect.width > 0f && detailsRect.Contains(Event.current.mousePosition) ||
                                searchResultsRect.width > 0f && searchResultsRect.Contains(Event.current.mousePosition);

        HandleGraphInput(scrollRect, viewRect, graphInputBlocked);
        Widgets.BeginScrollView(scrollRect, ref graphScroll, viewRect);
        FocusPendingProject(layout, scrollRect, viewRect, detailsRect.width);
        EnsureSelectedVisible(layout, scrollRect, viewRect, detailsRect.width);
        DrawProgressionBackdrop(projects, layout, viewRect);
        DrawConnections(baseRoutes, zoom);
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
            if (route.IsBundled && points.Count > 2)
            {
                var junction = points[2];
                var junctionSize = Mathf.Max(5f, thickness + 2f);
                Widgets.DrawBoxSolid(new Rect(junction.x - junctionSize * 0.5f,
                    junction.y - junctionSize * 0.5f, junctionSize, junctionSize), color);
            }
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
        bool allowInput)
    {
        var isHidden = project.IsHidden;
        var isCurrent = Find.ResearchManager.IsCurrentProject(project);
        var normalizedSearch = ResearchGraphModel.NormalizeQuery(search);
        var matches = !isHidden && ResearchGraphModel.Matches(project, normalizedSearch);
        var statusColor = StatusColor(project, isCurrent);
        var searchActive = !normalizedSearch.NullOrEmpty();
        var contentAlpha = 1f;
        if (searchActive && !matches)
        {
            contentAlpha = 0.42f;
        }

        var nodeSurface = Color.Lerp(NodeSurface, Background, (1f - contentAlpha) * 0.74f);
        if (Mouse.IsOver(rect) && allowInput)
        {
            nodeSurface = Color.Lerp(nodeSurface, NodeSurfaceHover, 0.72f);
        }

        if (selectedProject == project)
        {
            Widgets.DrawShadowAround(rect.ExpandedBy(5f));
            Widgets.DrawBoxSolid(rect.ExpandedBy(4f), new Color(SelectedBorder.r, SelectedBorder.g, SelectedBorder.b, 0.88f));
        }
        else if (searchActive && matches)
        {
            Widgets.DrawBoxSolid(rect.ExpandedBy(3f), new Color(SearchBorder.r, SearchBorder.g, SearchBorder.b, 0.80f));
        }
        Widgets.DrawBoxSolid(rect, nodeSurface);
        DrawBorder(rect, Border, 1);

        if (isHidden)
        {
            DrawUnknownNode(rect, allowInput);
            return;
        }

        Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 4f, Mathf.Max(3f, 4f * zoom), rect.height - 8f),
            new Color(statusColor.r, statusColor.g, statusColor.b, contentAlpha));
        if (allowInput && Widgets.ButtonInvisible(rect))
        {
            selectedProject = project;
            pendingEnsureVisible = project;
            ResetDetailsScroll();
        }

        if (selectedProject == project)
        {
            var markerWidth = Mathf.Min(rect.width * 0.42f, 92f * zoom);
            Widgets.DrawBoxSolid(new Rect(rect.center.x - markerWidth * 0.5f, rect.y - 6f,
                markerWidth, 5f), SelectedBorder);
        }
        else if (searchActive && matches)
        {
            var markerWidth = Mathf.Min(rect.width * 0.32f, 72f * zoom);
            Widgets.DrawBoxSolid(new Rect(rect.center.x - markerWidth * 0.5f, rect.y - 5f,
                markerWidth, 3f), SearchBorder);
        }

        var padding = Mathf.Max(5f, 8f * zoom);
        var inner = rect.ContractedBy(padding);
        Text.Font = zoom < 0.72f ? GameFont.Tiny : GameFont.Small;
        Text.Anchor = TextAnchor.UpperCenter;
        GUI.color = new Color(1f, 1f, 1f, contentAlpha);
        Widgets.Label(new Rect(inner.x, inner.y, inner.width, 28f * zoom), project.LabelCap);
        var requirements = BuildNodeRequirements(project);
        var hasBadges = requirements.Count > 0 && zoom >= 0.72f;
        if (hasBadges)
        {
            DrawNodeRequirementBadges(requirements,
                new Rect(inner.x, inner.y + 27f * zoom, inner.width, 19f * zoom), contentAlpha);
        }
        DrawUnlockIcons(project,
            new Rect(inner.x, inner.y + (hasBadges ? 49f : 30f) * zoom,
                inner.width, (hasBadges ? 25f : 42f) * zoom), contentAlpha);

        var progressRect = new Rect(inner.x, rect.yMax - 13f * zoom, inner.width, Mathf.Max(5f, 8f * zoom));
        Widgets.DrawBoxSolid(progressRect, TrackSurface);
        var filled = progressRect;
        filled.width *= Mathf.Clamp01(project.ProgressPercent);
        var progressColor = project.IsFinished ? Completed : isCurrent ? Current : Progress;
        Widgets.DrawBoxSolid(filled, new Color(progressColor.r, progressColor.g, progressColor.b, contentAlpha));

        Text.Font = GameFont.Tiny;
        Text.Anchor = TextAnchor.MiddleCenter;
        var status = isCurrent ? $"{StatusLabel(project, true)} · {project.ProgressPercent:P0}" : StatusLabel(project, false);
        if (zoom >= 0.72f)
        {
            var statusHeight = Mathf.Max(Text.LineHeight + 2f, 22f);
            GUI.color = new Color(statusColor.r, statusColor.g, statusColor.b, contentAlpha);
            Widgets.Label(new Rect(inner.x, progressRect.y - statusHeight - 2f, inner.width, statusHeight), status);
        }
        if (allowInput)
        {
            TooltipHandler.TipRegion(rect, NodeTooltip(project));
        }
        Text.Anchor = TextAnchor.UpperLeft;
        Text.Font = GameFont.Small;
        GUI.color = Color.white;
    }

    private static void DrawUnknownNode(Rect rect, bool allowInput)
    {
        Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 4f, Mathf.Max(3f, 4f * zoom), rect.height - 8f),
            new Color(Locked.r, Locked.g, Locked.b, 0.62f));
        var inner = rect.ContractedBy(Mathf.Max(6f, 9f * zoom));
        Text.Anchor = TextAnchor.UpperCenter;
        Text.Font = zoom < 0.72f ? GameFont.Tiny : GameFont.Small;
        GUI.color = new Color(0.76f, 0.78f, 0.78f, 0.82f);
        Widgets.Label(new Rect(inner.x, inner.y + 2f, inner.width, 28f * zoom), "RR_UnknownResearch".Translate());
        Text.Font = GameFont.Medium;
        GUI.color = new Color(0.58f, 0.62f, 0.63f, 0.72f);
        Widgets.Label(new Rect(inner.x, inner.y + 33f * zoom, inner.width, 34f * zoom), "?");
        Text.Font = GameFont.Tiny;
        GUI.color = Locked;
        Widgets.Label(new Rect(inner.x, rect.yMax - 36f * zoom, inner.width, 22f * zoom),
            "RR_AwaitingDiscovery".Translate());
        var progressRect = new Rect(inner.x, rect.yMax - 13f * zoom, inner.width, Mathf.Max(5f, 8f * zoom));
        Widgets.DrawBoxSolid(progressRect, TrackSurface);
        if (allowInput)
        {
            TooltipHandler.TipRegion(rect, "UnknownResearchDescription".Translate());
        }
        Text.Anchor = TextAnchor.UpperLeft;
        Text.Font = GameFont.Small;
        GUI.color = Color.white;
    }

    private static List<SpecialRequirement> BuildNodeRequirements(ResearchProjectDef project)
    {
        var requirements = BuildSpecialRequirements(project);
        var prerequisites = project.prerequisites ?? new List<ResearchProjectDef>();
        var finishedPrerequisites = prerequisites.Count(prerequisite => prerequisite.IsFinished);
        if (!project.PrerequisitesCompleted && prerequisites.Count > 0)
        {
            requirements.Insert(0, new SpecialRequirement(
                "RR_PrerequisitesProgress".Translate(finishedPrerequisites, prerequisites.Count).ToString(),
                "RR_BadgePrerequisite".Translate(finishedPrerequisites, prerequisites.Count).ToString(),
                false,
                Danger));
        }

        if (!project.IsFinished && !project.PlayerHasAnyAppropriateResearchBench)
        {
            requirements.Add(new SpecialRequirement(
                "RR_MissingResearchBench".Translate().ToString(),
                "RR_BadgeEnvironment".Translate().ToString(),
                false,
                Danger));
        }

        return requirements;
    }

    private static void DrawNodeRequirementBadges(
        IReadOnlyList<SpecialRequirement> requirements,
        Rect area,
        float contentAlpha)
    {
        Text.Font = GameFont.Tiny;
        Text.Anchor = TextAnchor.MiddleCenter;
        var x = area.x;
        var drawn = 0;
        foreach (var requirement in requirements)
        {
            var label = requirement.Badge;
            var width = Mathf.Clamp(Text.CalcSize(label).x + 12f * zoom, 30f * zoom, 92f * zoom);
            if (x + width > area.xMax - 26f * zoom)
            {
                break;
            }

            var rect = new Rect(x, area.y, width, area.height);
            var accent = requirement.Met ? Completed : requirement.Accent;
            Widgets.DrawBoxSolid(rect, new Color(accent.r, accent.g, accent.b, 0.16f * contentAlpha));
            DrawBorder(rect, new Color(accent.r, accent.g, accent.b, 0.72f * contentAlpha), 1);
            GUI.color = new Color(accent.r, accent.g, accent.b, contentAlpha);
            Widgets.Label(rect, label);
            TooltipHandler.TipRegion(rect, requirement.Label);
            x += width + 4f * zoom;
            drawn++;
        }

        if (drawn < requirements.Count)
        {
            var moreRect = new Rect(area.xMax - 25f * zoom, area.y, 25f * zoom, area.height);
            GUI.color = new Color(1f, 1f, 1f, contentAlpha);
            Widgets.Label(moreRect, $"+{requirements.Count - drawn}");
            TooltipHandler.TipRegion(moreRect,
                string.Join("\n", requirements.Skip(drawn).Select(requirement => requirement.Label)));
        }
        GUI.color = Color.white;
        Text.Anchor = TextAnchor.UpperLeft;
    }

    private static void DrawUnlockIcons(ResearchProjectDef project, Rect area, float contentAlpha)
    {
        var unlocks = ResearchGraphModel.UnlocksFor(project);
        var withIcons = unlocks.Where(unlock =>
            Widgets.CanDrawIconFor(unlock.DisplayDefinition) || unlock.Icon != null).Take(4).ToList();
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
            GUI.color = new Color(1f, 1f, 1f, contentAlpha);
            DrawDefinitionIcon(iconRect, unlock.DisplayDefinition, unlock.Icon);
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
        GUI.color = Color.white;
    }

    private static string NodeTooltip(ResearchProjectDef project)
    {
        var unlocks = ResearchGraphModel.UnlocksFor(project);
        var customUnlocks = project.customUnlockTexts ?? new List<string>();
        var unlockLines = unlocks.Select(unlock => $"• {unlock.Label}")
            .Concat(customUnlocks.Select(text => $"• {text}"))
            .ToList();
        var unlockText = unlockLines.Count == 0
            ? "RR_NoUnlocks".Translate().ToString()
            : string.Join("\n", unlockLines);
        var progressText = "RR_Progress".Translate(project.ProgressPercent.ToString("P0"));
        var unlockHeading = "RR_Unlocks".Translate();
        var requirements = BuildNodeRequirements(project);
        var requirementText = requirements.Count == 0
            ? string.Empty
            : $"\n\n{"RR_ResearchRequirements".Translate()}\n" +
              string.Join("\n", requirements.Select(requirement =>
                  $"{(requirement.Met ? "✓" : "•")} {requirement.Label}"));
        return $"{project.LabelCap}\n{progressText}{requirementText}\n\n{unlockHeading}\n{unlockText}";
    }

    private static List<SpecialRequirement> BuildSpecialRequirements(ResearchProjectDef project)
    {
        var requirements = new List<SpecialRequirement>();
        if (project.TechprintCount > 0)
        {
            requirements.Add(new SpecialRequirement(
                "RR_TechprintsProgress".Translate(project.TechprintsApplied, project.TechprintCount).ToString(),
                "RR_BadgeTechprint".Translate(project.TechprintsApplied, project.TechprintCount).ToString(),
                project.TechprintRequirementMet,
                TechprintAccent));
        }

        if (project.RequiredAnalyzedThingCount > 0)
        {
            var analyzedLabels = project.requiredAnalyzed == null
                ? string.Empty
                : string.Join(", ", project.requiredAnalyzed.Select(thing => thing.LabelCap));
            requirements.Add(new SpecialRequirement(
                "RR_AnalysisProgress".Translate(
                    project.AnalyzedThingsCompleted,
                    project.RequiredAnalyzedThingCount,
                    analyzedLabels).ToString(),
                "RR_BadgeAnalysis".Translate(
                    project.AnalyzedThingsCompleted,
                    project.RequiredAnalyzedThingCount).ToString(),
                project.AnalyzedThingsRequirementsMet,
                AnalysisAccent));
        }

        if (project.requiresMechanitor)
        {
            requirements.Add(new SpecialRequirement(
                "RR_MechanitorRequirement".Translate().ToString(),
                "RR_BadgeMechanitor".Translate().ToString(),
                project.PlayerMechanitorRequirementMet,
                MechanitorAccent));
        }

        if (project.requireGravEngineInspected)
        {
            requirements.Add(new SpecialRequirement(
                "RR_GravEngineInspection".Translate().ToString(),
                "RR_BadgeInspection".Translate().ToString(),
                project.InspectionRequirementsMet,
                Current));
        }

        return requirements;
    }

    private static void DrawDetails(Rect rect)
    {
        DrawPanel(rect, PanelSurface, Border);
        Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 3f), SelectedBorder);
        var inner = rect.ContractedBy(12f);
        if (DrawToolbarButton(new Rect(inner.xMax - 28f, inner.y, 28f, 28f), "×"))
        {
            selectedProject = null;
            return;
        }

        var project = selectedProject!;
        var isCurrentProject = Find.ResearchManager.IsCurrentProject(project);
        var projectStatus = StatusColor(project, isCurrentProject);
        Text.Font = GameFont.Tiny;
        GUI.color = projectStatus;
        Widgets.Label(new Rect(inner.x, inner.y, inner.width - 34f, 16f), StatusLabel(project, isCurrentProject));
        GUI.color = Color.white;
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(inner.x, inner.y + 15f, inner.width - 34f, 34f), project.LabelCap);
        Text.Font = GameFont.Small;

        var contentWidth = inner.width - 20f;
        var description = project.description.NullOrEmpty() ? project.LabelCap.ToString() : project.description;
        var descriptionHeight = Text.CalcHeight(description, contentWidth);
        var prerequisites = project.prerequisites ?? new List<ResearchProjectDef>();
        var unlocks = ResearchGraphModel.UnlocksFor(project);
        var unlockGroups = ResearchGraphModel.UnlockGroupsFor(project);
        var customUnlocks = project.customUnlockTexts ?? new List<string>();
        var techprintFactions = TechprintFactionsFor(project);
        var researchFacilities = project.requiredResearchFacilities ?? new List<ThingDef>();
        var specialRequirements = BuildSpecialRequirements(project);
        var facilityCount = researchFacilities.Count;
        var missingResearchBench = !project.IsFinished && !project.PlayerHasAnyAppropriateResearchBench;
        var requirementRows = (project.requiredResearchBuilding != null ? 1 : 0) + facilityCount + specialRequirements.Count;
        var requirementLabels = (project.requiredResearchBuilding != null ? 1 : 0) + (facilityCount > 0 ? 1 : 0);
        var hasRequirements = requirementRows > 0 || missingResearchBench;
        var requirementHeight = !hasRequirements
            ? 0f
            : 52f + requirementRows * 29f + requirementLabels * 25f +
              (missingResearchBench ? 30f : 0f);
        var techprintInfoHeight = project.TechprintCount > 0
            ? 140f + techprintFactions.Count * 29f
            : 0f;
        var showUnlockGroupHeadings = unlockGroups.Count > 1 ||
                                      unlockGroups.Any(group => group.AdditionalPrerequisites.Count > 0);
        var unlockGroupHeight = showUnlockGroupHeadings ? unlockGroups.Count * 31f : 0f;
        var viewHeight = 310f + descriptionHeight + requirementHeight + techprintInfoHeight +
                         prerequisites.Count * 29f + unlocks.Count * 29f +
                         customUnlocks.Count * 29f + unlockGroupHeight;
        var scrollOuter = new Rect(inner.x, inner.y + 52f, inner.width, inner.height - 104f);
        UpdateDetailsScroll(scrollOuter, viewHeight);
        var scrollBeforeControls = detailsScroll.y;
        Widgets.BeginScrollView(scrollOuter, ref detailsScroll, new Rect(0f, 0f, contentWidth, viewHeight));

        var y = detailsBounce;
        var costLabel = project.knowledgeCategory == null
            ? "RR_Cost".Translate(project.CostApparent.ToString("0"))
            : "RR_KnowledgeCost".Translate(project.knowledgeCategory.LabelCap, project.CostApparent.ToString("0"));
        Widgets.Label(new Rect(0f, y, contentWidth, 28f), costLabel);
        y += 32f;
        DrawDetailedProgress(project, new Rect(0f, y, contentWidth, 30f));
        y += 44f;
        var sourceName = project.modContentPack?.Name ?? "RimWorld";
        DrawInfoStrip(new Rect(0f, y, contentWidth, 25f),
            "RR_ContentSource".Translate(sourceName).ToString(), Available);
        y += 33f;
        Widgets.Label(new Rect(0f, y, contentWidth, descriptionHeight), description);
        y += descriptionHeight + 18f;

        if (hasRequirements)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, contentWidth, 30f), "RR_ResearchRequirements".Translate());
            y += 32f;
            Text.Font = GameFont.Small;
            if (project.requiredResearchBuilding != null)
            {
                Widgets.Label(new Rect(0f, y, contentWidth, 24f), "RR_RequiredBench".Translate());
                y += 25f;
                DrawDefRequirementRow(project.requiredResearchBuilding, !missingResearchBench, ref y, contentWidth);
            }

            if (facilityCount > 0)
            {
                Widgets.Label(new Rect(0f, y, contentWidth, 24f), "RR_RequiredFacilities".Translate());
                y += 25f;
                foreach (var facility in researchFacilities)
                {
                    DrawDefRequirementRow(facility, !missingResearchBench, ref y, contentWidth);
                }
            }

            foreach (var requirement in specialRequirements)
            {
                DrawRequirementRow(requirement.Label, requirement.Met, ref y, contentWidth);
            }

            if (missingResearchBench)
            {
                DrawInfoStrip(new Rect(0f, y, contentWidth, 28f),
                    "RR_MissingResearchBench".Translate().ToString(), Danger);
                y += 30f;
            }
            y += 10f;
        }

        if (project.TechprintCount > 0)
        {
            Text.Font = GameFont.Medium;
            GUI.color = TechprintAccent;
            Widgets.Label(new Rect(0f, y, contentWidth, 30f), "RR_TechprintSources".Translate());
            GUI.color = Color.white;
            y += 32f;
            Text.Font = GameFont.Small;
            if (project.Techprint != null)
            {
                DrawDefRow(project.Techprint, ref y, contentWidth,
                    "RR_TechprintItem".Translate(project.Techprint.LabelCap).ToString(), project.Techprint.uiIcon);
            }
            Text.Font = GameFont.Tiny;
            var sourceHint = "ResearchTechprintsFromFactions".Translate().ToString();
            var sourceHintHeight = Text.CalcHeight(sourceHint, contentWidth);
            Widgets.Label(new Rect(0f, y, contentWidth, sourceHintHeight), sourceHint);
            y += sourceHintHeight + 5f;
            if (techprintFactions.Count == 0)
            {
                GUI.color = MutedText;
                Widgets.Label(new Rect(0f, y, contentWidth, 25f), "RR_NoKnownTechprintFaction".Translate());
                GUI.color = Color.white;
                y += 29f;
            }
            else
            {
                foreach (var faction in techprintFactions)
                {
                    DrawFactionRow(faction, ref y, contentWidth);
                }
            }
            Text.Font = GameFont.Small;
            y += 8f;
        }

        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(0f, y, contentWidth, 30f), "RR_Unlocks".Translate());
        y += 32f;
        Text.Font = GameFont.Small;
        if (unlocks.Count == 0 && customUnlocks.Count == 0)
        {
            Widgets.Label(new Rect(0f, y, contentWidth, 24f), "RR_NoUnlocks".Translate());
            y += 25f;
        }
        else
        {
            if (unlockGroups.Count == 0)
            {
                foreach (var unlock in unlocks)
                {
                    DrawDefRow(unlock.DisplayDefinition, ref y, contentWidth, unlock.Label, unlock.Icon);
                }
            }
            else
            {
                foreach (var group in unlockGroups)
                {
                    if (showUnlockGroupHeadings)
                    {
                        DrawUnlockGroupHeader(group, ref y, contentWidth);
                    }
                    foreach (var unlock in group.Unlocks)
                    {
                        DrawDefRow(unlock.DisplayDefinition, ref y, contentWidth, unlock.Label, unlock.Icon);
                    }
                }
            }
            foreach (var customUnlock in customUnlocks)
            {
                DrawTextUnlockRow(customUnlock, ref y, contentWidth);
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
                if (DrawPrerequisiteButton(new Rect(0f, y, contentWidth, 27f), prerequisite))
                {
                    SwitchTab(prerequisite.tab);
                    selectedProject = prerequisite;
                    pendingFocus = prerequisite;
                    ResetDetailsScroll();
                }
                y += 29f;
            }
        }
        Widgets.EndScrollView();
        if (Mathf.Abs(detailsScroll.y - scrollBeforeControls) > 0.1f)
        {
            detailsScrollTarget = detailsScroll.y;
            detailsBounce = 0f;
            detailsBounceVelocity = 0f;
        }
        DrawDetailsEdgeFeedback(scrollOuter);

        var buttonRect = new Rect(inner.x, rect.yMax - 44f, inner.width, 32f);
        var isCurrent = Find.ResearchManager.IsCurrentProject(project);
        var enabled = isCurrent || !project.IsFinished && project.CanStartNow;
        var buttonLabel = isCurrent ? "RR_StopResearch".Translate().ToString() : "RR_StartResearch".Translate().ToString();
        if (DrawActionButton(buttonRect, buttonLabel, enabled, isCurrent))
        {
            if (isCurrent)
            {
                Find.ResearchManager.StopProject(project);
            }
            else
            {
                Find.ResearchManager.SetCurrentProject(project);
            }
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
        if (Mouse.IsOver(row))
        {
            Widgets.DrawBoxSolid(row, NodeSurfaceHover);
            Widgets.DrawBoxSolid(new Rect(row.x, row.y + 3f, 3f, row.height - 6f), Available);
        }
        var infoRect = new Rect(row.xMax - Widgets.InfoCardButtonSize, row.y + 1f,
            Widgets.InfoCardButtonSize, Widgets.InfoCardButtonSize);
        var clickRect = new Rect(row.x, row.y, row.width - Widgets.InfoCardButtonSize - 4f, row.height);
        if (Widgets.ButtonInvisible(clickRect))
        {
            Find.WindowStack.Add(new Dialog_InfoCard(definition, null));
        }

        DrawDefinitionIcon(new Rect(row.x + 1f, row.y + 1f, 24f, 24f), definition, icon);
        Widgets.Label(new Rect(row.x + 31f, row.y + 2f, row.width - 64f, 24f),
            label ?? definition.LabelCap.ToString());
        Widgets.InfoCardButton(infoRect, definition);
        y += 29f;
    }

    private static void DrawDefRequirementRow(Def definition, bool met, ref float y, float contentWidth)
    {
        var row = new Rect(0f, y, contentWidth, 27f);
        var accent = met ? Completed : Danger;
        Widgets.DrawBoxSolid(row, new Color(accent.r, accent.g, accent.b, met ? 0.08f : 0.15f));
        Widgets.DrawBoxSolid(new Rect(row.x, row.y + 3f, 4f, row.height - 6f), accent);
        DrawDefinitionIcon(new Rect(row.x + 8f, row.y + 1f, 24f, 24f), definition, null);
        GUI.color = met ? Color.white : new Color(1f, 0.66f, 0.62f);
        Widgets.Label(new Rect(row.x + 38f, row.y + 2f, row.width - 72f, 24f), definition.LabelCap);
        GUI.color = Color.white;
        var infoRect = new Rect(row.xMax - Widgets.InfoCardButtonSize, row.y + 1f,
            Widgets.InfoCardButtonSize, Widgets.InfoCardButtonSize);
        Widgets.InfoCardButton(infoRect, definition);
        if (Widgets.ButtonInvisible(new Rect(row.x, row.y, row.width - Widgets.InfoCardButtonSize - 4f, row.height)))
        {
            Find.WindowStack.Add(new Dialog_InfoCard(definition, null));
        }
        y += 29f;
    }

    private static void DrawInfoStrip(Rect rect, string label, Color accent)
    {
        Widgets.DrawBoxSolid(rect, new Color(accent.r, accent.g, accent.b, 0.10f));
        Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 3f, 4f, rect.height - 6f), accent);
        GUI.color = new Color(0.90f, 0.92f, 0.90f);
        Widgets.Label(new Rect(rect.x + 11f, rect.y + 2f, rect.width - 14f, rect.height - 3f), label);
        GUI.color = Color.white;
    }

    private static List<Faction> TechprintFactionsFor(ResearchProjectDef project)
    {
        var result = new List<Faction>();
        if (project.heldByFactionCategoryTags == null)
        {
            return result;
        }

        foreach (var tag in project.heldByFactionCategoryTags)
        {
            foreach (var faction in Find.FactionManager.AllFactionsInViewOrder.Where(faction =>
                         faction.def.categoryTag == tag))
            {
                if (!result.Contains(faction))
                {
                    result.Add(faction);
                }
            }
        }
        return result;
    }

    private static void DrawFactionRow(Faction faction, ref float y, float contentWidth)
    {
        var row = new Rect(0f, y, contentWidth, 27f);
        if (Mouse.IsOver(row))
        {
            Widgets.DrawBoxSolid(row, NodeSurfaceHover);
        }
        FactionUIUtility.DrawFactionIconWithTooltip(new Rect(row.x + 2f, row.y + 2f, 23f, 23f), faction);
        Widgets.Label(new Rect(row.x + 32f, row.y + 2f, row.width - 34f, 24f), faction.Name);
        y += 29f;
    }

    private static void DrawUnlockGroupHeader(ResearchUnlockGroup group, ref float y, float contentWidth)
    {
        var direct = group.AdditionalPrerequisites.Count == 0;
        var met = direct || group.AdditionalPrerequisites.All(prerequisite => prerequisite.IsFinished);
        var accent = direct ? Available : met ? Completed : Danger;
        var prerequisites = string.Join(", ", group.AdditionalPrerequisites.Select(prerequisite => prerequisite.LabelCap));
        var label = direct
            ? "RR_DirectUnlocks".Translate().ToString()
            : "RR_LinkedUnlocks".Translate(prerequisites).ToString();
        DrawInfoStrip(new Rect(0f, y, contentWidth, 27f), label, accent);
        if (!direct)
        {
            TooltipHandler.TipRegion(new Rect(0f, y, contentWidth, 27f),
                "RR_LinkedUnlocksTip".Translate(prerequisites));
        }
        y += 31f;
    }

    private static bool DrawPrerequisiteButton(Rect rect, ResearchProjectDef prerequisite)
    {
        var met = prerequisite.IsFinished;
        var accent = met ? Completed : Danger;
        Widgets.DrawBoxSolid(rect, Mouse.IsOver(rect)
            ? new Color(accent.r, accent.g, accent.b, 0.17f)
            : new Color(accent.r, accent.g, accent.b, 0.09f));
        DrawBorder(rect, new Color(accent.r, accent.g, accent.b, 0.62f), 1);
        Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 4f, 4f, rect.height - 8f), accent);
        GUI.color = met ? new Color(0.82f, 0.94f, 0.84f) : new Color(1f, 0.67f, 0.64f);
        Widgets.Label(new Rect(rect.x + 12f, rect.y + 2f, rect.width - 16f, rect.height - 3f),
            $"{(met ? "✓" : "•")} {prerequisite.LabelCap}");
        GUI.color = Color.white;
        return Widgets.ButtonInvisible(rect);
    }

    private static void DrawRequirementRow(string label, bool met, ref float y, float contentWidth)
    {
        var row = new Rect(0f, y, contentWidth, 27f);
        Widgets.DrawBoxSolid(new Rect(row.x + 2f, row.y + 6f, 4f, row.height - 12f), met ? Completed : Current);
        GUI.color = met ? new Color(0.78f, 0.86f, 0.78f) : new Color(1f, 0.72f, 0.48f);
        Widgets.Label(new Rect(row.x + 13f, row.y + 2f, row.width - 13f, 24f), label);
        GUI.color = Color.white;
        y += 29f;
    }

    private static void DrawTextUnlockRow(string label, ref float y, float contentWidth)
    {
        var row = new Rect(0f, y, contentWidth, 27f);
        Widgets.DrawBoxSolid(new Rect(row.x + 2f, row.y + 6f, 4f, row.height - 12f), Available);
        Widgets.Label(new Rect(row.x + 13f, row.y + 2f, row.width - 13f, 24f), label);
        y += 29f;
    }

    private static void DrawDefinitionIcon(Rect rect, Def definition, Texture2D? fallback)
    {
        if (Widgets.CanDrawIconFor(definition))
        {
            Widgets.DefIcon(rect, definition);
            return;
        }

        var resolved = fallback ?? (definition as BuildableDef)?.uiIcon;
        if (resolved != null)
        {
            Widgets.DrawTextureFitted(rect, resolved, 1f);
        }
    }

    private static void DrawDetailedProgress(ResearchProjectDef project, Rect rect)
    {
        Widgets.DrawBoxSolid(rect, TrackSurface);
        var fillRect = rect.ContractedBy(2f);
        fillRect.width *= Mathf.Clamp01(project.ProgressPercent);
        var isCurrent = Find.ResearchManager.IsCurrentProject(project);
        Widgets.DrawBoxSolid(fillRect,
            project.IsFinished ? Completed : isCurrent ? Current : Progress);

        var progress = Mathf.Min(project.ProgressApparent, project.CostApparent);
        var pointText = $"{progress:0} / {project.CostApparent:0} ({project.ProgressPercent:P0})";
        var completedText = "RR_Completed".Translate();
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(rect, project.IsFinished ? $"{completedText} · {pointText}" : pointText);
        Text.Anchor = TextAnchor.UpperLeft;
    }

    private static void UpdateDetailsScroll(Rect scrollRect, float viewHeight)
    {
        var maximum = Mathf.Max(0f, viewHeight - scrollRect.height);
        var currentEvent = Event.current;
        var wheelDelta = pendingDetailsWheel;
        pendingDetailsWheel = 0f;
        if (Mathf.Abs(wheelDelta) < 0.01f && scrollRect.Contains(currentEvent.mousePosition) &&
            currentEvent.type == EventType.ScrollWheel)
        {
            wheelDelta = currentEvent.delta.y;
            currentEvent.Use();
        }

        if (Mathf.Abs(wheelDelta) >= 0.01f)
        {
            var movement = wheelDelta * 42f;
            var proposed = detailsScrollTarget + movement;
            if (proposed < 0f)
            {
                detailsBounce = Mathf.Clamp(detailsBounce + Mathf.Min(12f, -proposed * 0.18f), 0f, 12f);
            }
            else if (proposed > maximum)
            {
                detailsBounce = Mathf.Clamp(detailsBounce - Mathf.Min(12f, (proposed - maximum) * 0.18f), -12f, 0f);
            }

            detailsScrollTarget = Mathf.Clamp(proposed, 0f, maximum);
        }

        detailsScrollTarget = Mathf.Clamp(detailsScrollTarget, 0f, maximum);
        var deltaTime = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        var smoothing = 1f - Mathf.Exp(-18f * deltaTime);
        detailsScroll.y = Mathf.Lerp(detailsScroll.y, detailsScrollTarget, smoothing);
        detailsScroll.x = 0f;
        detailsBounce = Mathf.SmoothDamp(detailsBounce, 0f, ref detailsBounceVelocity,
            0.14f, 120f, deltaTime);
    }

    private static void DrawDetailsEdgeFeedback(Rect scrollRect)
    {
        if (Mathf.Abs(detailsBounce) < 0.25f)
        {
            return;
        }

        var alpha = Mathf.Clamp01(Mathf.Abs(detailsBounce) / 12f) * 0.55f;
        var y = detailsBounce > 0f ? scrollRect.y : scrollRect.yMax - 5f;
        Widgets.DrawBoxSolid(new Rect(scrollRect.x + 3f, y, scrollRect.width - 6f, 5f),
            new Color(Available.r, Available.g, Available.b, alpha));
    }

    private static void CaptureDetailsWheel(Rect detailsRect)
    {
        var currentEvent = Event.current;
        if (detailsRect.width <= 0f || !detailsRect.Contains(currentEvent.mousePosition) ||
            currentEvent.type != EventType.ScrollWheel)
        {
            return;
        }

        pendingDetailsWheel += currentEvent.delta.y;
        currentEvent.Use();
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
            else if (graphRect.Contains(currentEvent.mousePosition))
            {
                // Node buttons run later in the same event and will select themselves again.
                // A genuine empty-canvas click therefore clears the details focus cleanly.
                selectedProject = null;
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

    private static void DrawGraphBackdrop(Rect rect)
    {
        Widgets.DrawBoxSolid(rect, GraphSurface);
        var grid = new Color(0.52f, 0.50f, 0.44f, 0.075f);
        const float spacing = 44f;
        for (var x = rect.x + spacing; x < rect.xMax; x += spacing)
        {
            Widgets.DrawBoxSolid(new Rect(x, rect.y + 1f, 1f, rect.height - 2f), grid);
        }
        for (var y = rect.y + spacing; y < rect.yMax; y += spacing)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x + 1f, y, rect.width - 2f, 1f), grid);
        }
        DrawBorder(rect, Border, 1);
    }

    private static void DrawProgressionBackdrop(
        IReadOnlyList<ResearchProjectDef> projects,
        IReadOnlyDictionary<ResearchProjectDef, Rect> layout,
        Rect viewRect)
    {
        var knowledgeProjects = projects.Where(project => project.knowledgeCategory != null).ToList();
        var isKnowledgeTree = knowledgeProjects.Count > 0;
        var basicProjects = knowledgeProjects.Where(project =>
            project.knowledgeCategory.defName.Equals("Basic", StringComparison.OrdinalIgnoreCase)).ToList();
        var advancedProjects = knowledgeProjects.Where(project =>
            project.knowledgeCategory.defName.Equals("Advanced", StringComparison.OrdinalIgnoreCase)).ToList();

        var transition = viewRect.width * 0.62f;
        if (advancedProjects.Count > 0)
        {
            var advancedStart = advancedProjects.Min(project => layout[project].xMin);
            var basicEnd = basicProjects.Count > 0
                ? basicProjects.Max(project => layout[project].xMax)
                : advancedStart - 280f;
            transition = (basicEnd + advancedStart) * 0.5f;
        }

        const float bandWidth = 64f;
        for (var x = 0f; x < viewRect.width; x += bandWidth)
        {
            var center = x + bandWidth * 0.5f;
            Color tint;
            if (isKnowledgeTree)
            {
                var blend = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(transition - 420f, transition + 520f, center));
                tint = Color.Lerp(new Color(0f, 0f, 0f, 0.20f),
                    new Color(0.29f, 0.018f, 0.026f, 0.43f), blend);
            }
            else
            {
                var blend = Mathf.InverseLerp(0f, Mathf.Max(1f, viewRect.width), center);
                tint = Color.Lerp(new Color(0.015f, 0.02f, 0.02f, 0.08f),
                    new Color(0.20f, 0.12f, 0.035f, 0.16f), blend);
            }
            Widgets.DrawBoxSolid(new Rect(x, 0f, Mathf.Min(bandWidth + 1f, viewRect.width - x), viewRect.height), tint);
        }

        if (!isKnowledgeTree)
        {
            DrawZoneHeader(new Rect(18f, 12f, Mathf.Min(330f, viewRect.width - 36f), 44f),
                "RR_MainResearchZone".Translate().ToString(),
                "RR_MainResearchZoneHint".Translate().ToString(),
                Available,
                null);
            return;
        }

        var categoryGroups = knowledgeProjects.GroupBy(project => project.knowledgeCategory)
            .Select(group => new
            {
                Category = group.Key,
                Projects = group.ToList(),
                Center = group.Average(project => layout[project].center.x)
            })
            .OrderBy(group => group.Center)
            .ToList();
        for (var index = 0; index < categoryGroups.Count; index++)
        {
            var group = categoryGroups[index];
            var left = index == 0
                ? 8f
                : (categoryGroups[index - 1].Center + group.Center) * 0.5f;
            var right = index == categoryGroups.Count - 1
                ? viewRect.width - 8f
                : (group.Center + categoryGroups[index + 1].Center) * 0.5f;
            var accent = group.Category.color;
            var zoneRect = new Rect(left, 7f, Mathf.Max(80f, right - left), viewRect.height - 14f);
            Widgets.DrawBoxSolid(zoneRect, new Color(accent.r, accent.g, accent.b, 0.035f));
            DrawBorder(zoneRect, new Color(accent.r, accent.g, accent.b, 0.18f), 1);
            if (index > 0)
            {
                Widgets.DrawBoxSolid(new Rect(left - 2f, 7f, 4f, viewRect.height - 14f),
                    new Color(accent.r, accent.g, accent.b, 0.26f));
            }

            var headerWidth = Mathf.Min(310f, Mathf.Max(160f, zoneRect.width - 28f));
            DrawZoneHeader(new Rect(zoneRect.x + 14f, 12f, headerWidth, 44f),
                "RR_KnowledgeResearchZone".Translate(group.Category.LabelCap).ToString(),
                "RR_KnowledgeResearchZoneHint".Translate().ToString(),
                accent,
                group.Category.Tex);
        }
    }

    private static void DrawZoneHeader(Rect rect, string title, string subtitle, Color accent, Texture2D? icon)
    {
        Widgets.DrawBoxSolid(rect, new Color(0.07f, 0.075f, 0.073f, 0.96f));
        DrawBorder(rect, new Color(accent.r, accent.g, accent.b, 0.72f), 1);
        Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 3f), accent);
        var textX = rect.x + 11f;
        if (icon != null)
        {
            GUI.color = accent;
            GUI.DrawTexture(new Rect(rect.x + 9f, rect.y + 11f, 23f, 23f), icon, ScaleMode.ScaleToFit);
            GUI.color = Color.white;
            textX = rect.x + 39f;
        }
        Text.Font = GameFont.Small;
        GUI.color = new Color(accent.r, accent.g, accent.b, 0.98f);
        Widgets.Label(new Rect(textX, rect.y + 5f, rect.xMax - textX - 8f, 22f), title);
        Text.Font = GameFont.Tiny;
        GUI.color = MutedText;
        Widgets.Label(new Rect(textX, rect.y + 24f, rect.xMax - textX - 8f, 17f), subtitle);
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
    }

    private static void DrawPanel(Rect rect, Color fill, Color border)
    {
        Widgets.DrawBoxSolid(rect, fill);
        DrawBorder(rect, border, 1);
    }

    private static void DrawBorder(Rect rect, Color color, int thickness)
    {
        var size = Mathf.Max(1f, thickness);
        Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, size), color);
        Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - size, rect.width, size), color);
        Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, size, rect.height), color);
        Widgets.DrawBoxSolid(new Rect(rect.xMax - size, rect.y, size, rect.height), color);
    }

    private static bool DrawToolbarButton(Rect rect, string label, bool accent = false)
    {
        var hovered = Mouse.IsOver(rect);
        var fill = accent
            ? new Color(SelectedTab.r, SelectedTab.g, SelectedTab.b, hovered ? 1f : 0.88f)
            : hovered ? NodeSurfaceHover : NodeSurface;
        Widgets.DrawBoxSolid(rect, fill);
        DrawBorder(rect, accent ? new Color(SelectedBorder.r, SelectedBorder.g, SelectedBorder.b, 0.62f) : Border, 1);
        Text.Anchor = TextAnchor.MiddleCenter;
        GUI.color = accent ? SelectedBorder : Color.white;
        Widgets.Label(rect, label);
        GUI.color = Color.white;
        Text.Anchor = TextAnchor.UpperLeft;
        return Widgets.ButtonInvisible(rect);
    }

    private static bool DrawActionButton(Rect rect, string label, bool active, bool selected)
    {
        var hovered = active && Mouse.IsOver(rect);
        var fill = selected
            ? new Color(0.34f, 0.27f, 0.14f)
            : active
                ? (hovered ? new Color(0.31f, 0.29f, 0.22f) : SelectedTab)
                : new Color(NodeSurface.r, NodeSurface.g, NodeSurface.b, 0.72f);
        var border = selected || active ? SelectedBorder : Border;
        Widgets.DrawBoxSolid(rect, fill);
        DrawBorder(rect, new Color(border.r, border.g, border.b, active || selected ? 0.78f : 0.42f), 1);
        if (active || selected)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x + 8f, rect.yMax - 3f, rect.width - 16f, 3f), SelectedBorder);
        }
        Text.Anchor = TextAnchor.MiddleCenter;
        GUI.color = active || selected ? Color.white : MutedText;
        Widgets.Label(rect, label);
        GUI.color = Color.white;
        Text.Anchor = TextAnchor.UpperLeft;
        return active && Widgets.ButtonInvisible(rect);
    }

    private static Color StatusColor(ResearchProjectDef project, bool isCurrent)
    {
        return project.IsFinished ? Completed : isCurrent ? Current : project.CanStartNow ? Available : Locked;
    }

    private static string StatusLabel(ResearchProjectDef project, bool isCurrent)
    {
        var state = project.IsFinished
            ? "RR_Completed".Translate().ToString()
            : isCurrent
                ? "RR_CurrentResearch".Translate().ToString()
                : project.CanStartNow
                    ? "RR_Available".Translate().ToString()
                    : "RR_Locked".Translate().ToString();
        return project.knowledgeCategory == null
            ? state
            : $"{project.knowledgeCategory.LabelCap} · {state}";
    }

    private static void DrawStatusLegend(Rect rect)
    {
        Text.Font = GameFont.Tiny;
        Text.Anchor = TextAnchor.MiddleRight;
        var items = new[]
        {
            ("RR_Locked".Translate().ToString(), Locked),
            ("RR_Completed".Translate().ToString(), Completed),
            ("RR_Available".Translate().ToString(), Available)
        };
        var x = rect.xMax;
        foreach (var item in items)
        {
            var textWidth = Text.CalcSize(item.Item1).x;
            var itemWidth = textWidth + 18f;
            x -= itemWidth;
            Widgets.DrawBoxSolid(new Rect(x + 3f, rect.center.y - 3f, 6f, 6f), item.Item2);
            GUI.color = MutedText;
            Widgets.Label(new Rect(x + 13f, rect.y, textWidth, rect.height), item.Item1);
            GUI.color = Color.white;
            x -= 8f;
        }
        Text.Anchor = TextAnchor.UpperLeft;
    }

    private static void DrawHeaderContext(Rect rect)
    {
        GUI.color = MutedText;
        if (selectedTab != null && ResearchGraphModel.ProjectsFor(selectedTab).Any(project => project.knowledgeCategory != null))
        {
            var entries = DefDatabase<KnowledgeCategoryDef>.AllDefsListForReading
                .Select(category =>
                {
                    var current = Find.ResearchManager.GetProject(category);
                    var projectName = current?.LabelCap.ToString() ?? "RR_NoProjectShort".Translate().ToString();
                    return $"{category.LabelCap}: {projectName}";
                })
                .ToList();
            var text = string.Join("  ·  ", entries);
            Widgets.Label(rect, text);
            var hint = "RR_AnomalyConcurrentHint".Translate();
            TooltipHandler.TipRegion(rect, $"{hint}\n{text}");
        }
        else
        {
            Widgets.Label(rect, "RR_NavigationHint".Translate());
        }
        GUI.color = Color.white;
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
        ResearchProjectDef? current = null;
        var tabProjects = ResearchGraphModel.ProjectsFor(selectedTab);
        if (tabProjects.Any(project => project.knowledgeCategory != null))
        {
            if (selectedProject?.knowledgeCategory != null)
            {
                current = Find.ResearchManager.GetProject(selectedProject.knowledgeCategory);
            }

            current ??= tabProjects
                .Where(project => project.knowledgeCategory != null)
                .Select(project => project.knowledgeCategory)
                .Distinct()
                .Select(category => Find.ResearchManager.GetProject(category))
                .FirstOrDefault(project => project != null);
        }
        else
        {
            current = Find.ResearchManager.GetProject();
        }

        if (current == null)
        {
            Messages.Message("RR_NoCurrentResearch".Translate(), MessageTypeDefOf.NeutralEvent, false);
            return;
        }

        SwitchTab(current.tab);
        selectedProject = current;
        pendingFocus = current;
        ResetDetailsScroll();
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

    private static void SwitchTab(ResearchTabDef? tab)
    {
        if (selectedTab == tab)
        {
            return;
        }

        SaveCurrentTabState();
        selectedTab = tab;
        pendingFocus = null;
        pendingEnsureVisible = null;
        dragCandidate = false;
        draggingCanvas = false;
        searchFieldFocused = false;
        searchDropdownOpen = false;

        if (tab != null && TabStates.TryGetValue(tab, out var state))
        {
            graphScroll = state.GraphScroll;
            detailsScroll = state.DetailsScroll;
            detailsScrollTarget = detailsScroll.y;
            zoom = state.Zoom;
            search = state.Search;
            selectedProject = state.SelectedProject != null && state.SelectedProject.tab == tab
                ? state.SelectedProject
                : null;
        }
        else
        {
            graphScroll = Vector2.zero;
            detailsScroll = Vector2.zero;
            detailsScrollTarget = 0f;
            zoom = 1f;
            search = string.Empty;
            selectedProject = null;
        }

        detailsBounce = 0f;
        detailsBounceVelocity = 0f;
    }

    private static void SaveCurrentTabState()
    {
        if (selectedTab == null)
        {
            return;
        }

        TabStates[selectedTab] = new TabViewState(
            graphScroll,
            detailsScroll,
            zoom,
            search,
            selectedProject);
    }

    private static void ResetDetailsScroll()
    {
        detailsScroll = Vector2.zero;
        detailsScrollTarget = 0f;
        detailsBounce = 0f;
        detailsBounceVelocity = 0f;
    }

    private sealed class SearchResult
    {
        internal SearchResult(ResearchProjectDef project, string? matchedUnlock, int score)
        {
            Project = project;
            MatchedUnlock = matchedUnlock;
            Score = score;
        }

        internal ResearchProjectDef Project { get; }
        internal string? MatchedUnlock { get; }
        internal int Score { get; }
    }

    private sealed class SpecialRequirement
    {
        internal SpecialRequirement(string label, string badge, bool met, Color accent)
        {
            Label = label;
            Badge = badge;
            Met = met;
            Accent = accent;
        }

        internal string Label { get; }
        internal string Badge { get; }
        internal bool Met { get; }
        internal Color Accent { get; }
    }

    private sealed class TabViewState
    {
        internal TabViewState(
            Vector2 graphScroll,
            Vector2 detailsScroll,
            float zoom,
            string search,
            ResearchProjectDef? selectedProject)
        {
            GraphScroll = graphScroll;
            DetailsScroll = detailsScroll;
            Zoom = zoom;
            Search = search;
            SelectedProject = selectedProject;
        }

        internal Vector2 GraphScroll { get; }
        internal Vector2 DetailsScroll { get; }
        internal float Zoom { get; }
        internal string Search { get; }
        internal ResearchProjectDef? SelectedProject { get; }
    }
}
