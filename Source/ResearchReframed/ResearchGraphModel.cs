using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ResearchReframed;

internal sealed class ResearchUnlock
{
    internal ResearchUnlock(Def definition, Def displayDefinition, string label, Texture2D? icon)
    {
        Definition = definition;
        DisplayDefinition = displayDefinition;
        Label = label;
        Icon = icon;
    }

    internal Def Definition { get; }
    internal Def DisplayDefinition { get; }
    internal string Label { get; }
    internal Texture2D? Icon { get; }
}

internal static class ResearchGraphModel
{
    private static readonly Dictionary<ResearchProjectDef, IReadOnlyList<ResearchUnlock>> UnlockCache = new();

    internal static List<ResearchProjectDef> ProjectsFor(ResearchTabDef? tab)
    {
        return DefDatabase<ResearchProjectDef>.AllDefsListForReading
            .Where(project => project.tab == tab && IsVisible(project))
            .OrderBy(project => project.researchViewX)
            .ThenBy(project => project.researchViewY)
            .ThenBy(project => project.defName)
            .ToList();
    }

    internal static bool IsVisible(ResearchProjectDef project)
    {
        if (project.IsHidden)
        {
            return false;
        }

        return Find.Storyteller.difficulty.AllowedBy(project.hideWhen) ||
               Find.ResearchManager.IsCurrentProject(project);
    }

    internal static bool Matches(ResearchProjectDef project, string query)
    {
        query = NormalizeQuery(query);
        if (query.NullOrEmpty())
        {
            return true;
        }

        if (NormalizeQuery(project.LabelCap.ToString()).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            NormalizeQuery(project.defName).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return UnlocksFor(project).Any(unlock =>
            NormalizeQuery(unlock.Label).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            NormalizeQuery(unlock.Definition.defName).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
               (project.customUnlockTexts?.Any(text =>
                   NormalizeQuery(text).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ?? false);
    }

    internal static string NormalizeQuery(string? query)
    {
        if (query.NullOrEmpty())
        {
            return string.Empty;
        }

        return new string(query!.Where(character =>
                !char.IsWhiteSpace(character) && character != '\u200B' && character != '\uFEFF')
            .ToArray());
    }

    internal static IReadOnlyList<ResearchUnlock> UnlocksFor(ResearchProjectDef project)
    {
        if (UnlockCache.TryGetValue(project, out var cached))
        {
            return cached;
        }

        var result = new List<ResearchUnlock>();
        foreach (var definition in project.UnlockedDefs ?? Enumerable.Empty<Def>())
        {
            var displayDefinition = definition is RecipeDef recipe && recipe.ProducedThingDef != null
                ? recipe.ProducedThingDef
                : definition;
            var icon = IconFor(definition);
            result.Add(new ResearchUnlock(definition, displayDefinition, definition.LabelCap.ToString(), icon));
        }

        result.Sort((left, right) => string.Compare(left.Label, right.Label, StringComparison.CurrentCultureIgnoreCase));
        UnlockCache[project] = result;
        return result;
    }

    internal static HashSet<ResearchProjectDef> PrerequisitePath(ResearchProjectDef? project)
    {
        var result = new HashSet<ResearchProjectDef>();
        if (project != null)
        {
            AddPrerequisites(project, result);
        }
        return result;
    }

    internal static HashSet<ResearchProjectDef> DependentPath(ResearchProjectDef? project)
    {
        var result = new HashSet<ResearchProjectDef>();
        if (project == null)
        {
            return result;
        }

        var projects = DefDatabase<ResearchProjectDef>.AllDefsListForReading;
        AddDependents(project, projects, result);
        return result;
    }

    private static void AddPrerequisites(ResearchProjectDef project, ISet<ResearchProjectDef> result)
    {
        if (project.prerequisites == null)
        {
            return;
        }

        foreach (var prerequisite in project.prerequisites)
        {
            if (result.Add(prerequisite))
            {
                AddPrerequisites(prerequisite, result);
            }
        }
    }

    private static void AddDependents(
        ResearchProjectDef project,
        IReadOnlyCollection<ResearchProjectDef> projects,
        ISet<ResearchProjectDef> result)
    {
        foreach (var dependent in projects.Where(candidate =>
                     candidate.prerequisites != null && candidate.prerequisites.Contains(project)))
        {
            if (result.Add(dependent))
            {
                AddDependents(dependent, projects, result);
            }
        }
    }

    private static Texture2D? IconFor(Def definition)
    {
        if (definition is ThingDef thing)
        {
            return thing.uiIcon;
        }

        if (definition is RecipeDef recipe && recipe.ProducedThingDef != null)
        {
            return recipe.ProducedThingDef.uiIcon;
        }

        if (definition is BuildableDef buildable)
        {
            return buildable.uiIcon;
        }

        return null;
    }
}
