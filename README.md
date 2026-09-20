# 科技界面焕新 / Research Reframed

Research Reframed is a vanilla-styled, data-driven redesign of RimWorld's research interface for RimWorld 1.6.

## Goals

- Preserve RimWorld's research rules and save data.
- Discover standard `ResearchProjectDef` and `ResearchTabDef` content from other mods automatically.
- Improve readability with clearer nodes, status colors, search, a dedicated detail panel, and better use of screen space.
- Fall back to the vanilla research window if the custom renderer fails.
- Allow holding Shift while opening Research to use the vanilla interface.

## Current prototype

- Dynamic research tabs and projects.
- Search by translated label or `defName`.
- Prerequisite connections.
- Completed, current, available, and locked visual states.
- Research details and project selection.
- English and Simplified Chinese translations.

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
