# 科技界面焕新 / Research Reframed

Research Reframed is a RimWorld-compatible, data-driven redesign of the research interface for RimWorld 1.6. Its “Frontier Atlas” visual language combines matte frontier-industrial surfaces with bright, unambiguous information accents.

## Goals

- Preserve RimWorld's research rules and save data.
- Discover standard `ResearchProjectDef` and `ResearchTabDef` content from other mods automatically.
- Improve readability with neutral node surfaces, non-conflicting status/search/selection signals, a dedicated detail panel, and better use of screen space.
- Fall back to the vanilla research window if the custom renderer fails.
- Allow holding Shift to temporarily view the vanilla interface.

## Current prototype

- Near-full-screen research canvas that follows the current resolution and UI scale.
- Dynamic research tabs and projects, including standard content added by other mods.
- Vanilla-compatible hidden-project, difficulty, techprint, entity-analysis, mechanitor, grav-engine, and research-facility requirements.
- Unknown-project placeholders preserve the original tree shape without leaking undiscovered entity research.
- Color-coded node badges expose missing prerequisites, techprints, analysis, mechanitors, inspections, and research environments at a glance.
- Techprint faction sources, content-source attribution, and vanilla linked-unlock grouping are retained in the details panel.
- Anomaly-aware Basic/Advanced knowledge projects, dual active-project status, monolith-gated tab visibility, entity-discovery visibility, and a black-to-red knowledge gradient.
- Vanilla-colored unlock icons on nodes, persistent progress bars, and completed/current/available/locked states.
- Search by research name, `defName`, or unlocked content with ranked, clickable cross-tab results and stable graph layout.
- Layered, crossing-reduced graph layout with adaptive branch spacing, shared fan-out buses, obstacle-aware edge routing, directional arrows, and focus highlighting.
- Left-drag panning, zoom, fit-to-view, and focus-current-research controls.
- Per-tab view state for scroll position, zoom, selection, details position, and search.
- Smooth mouse-wheel details scrolling with restrained elastic edge feedback.
- Input-isolated overlay details panel with clickable vanilla info cards, complete special requirements, custom unlock text, exact research/knowledge progress, and start/stop project controls.
- English and Simplified Chinese translations.
- “Frontier Atlas” theme with a warm graphite canvas, restrained grid, cyan information accents, amber selection focus, and a persistent status legend.

## Compatibility strategy

Content mods that add standard research definitions should be discovered automatically. Mods that replace the entire research UI, such as Research Tree or ResearchPal, target the same screen and should not be enabled at the same time until explicit compatibility is implemented.

## Building

The project targets .NET Framework 4.7.2 and uses the authorized `Krafs.Rimworld.Ref` reference package, so proprietary RimWorld assemblies are not committed to this repository.

```powershell
dotnet build Source/ResearchReframed/ResearchReframed.csproj -c Release
```

The compiled assembly is written to `1.6/Assemblies/`.

## Runtime testing

Runtime testing requires a legally licensed copy of RimWorld 1.6. Static compilation cannot verify in-game rendering, interaction behavior, or compatibility with a live mod list.
