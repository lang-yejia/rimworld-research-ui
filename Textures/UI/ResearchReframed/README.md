# Frontier Atlas UI assets

These original transparent UI emblems support semantic states that do not map cleanly to a single RimWorld `Def` icon:

- `UnknownResearch.png`: undiscovered research placeholder.
- `PrerequisiteV2.png`: missing research-project prerequisites.
- `ResearchEnvironment.png`: missing ordinary research bench or facility setup.

They use a shared matte graphite, aged brass, cyan-signal, and amber-instrument visual language. Final files are 128×128 transparent PNGs and are intended to remain readable at 24–48 px.

Quantitative requirements such as techprints, analyzed chips, mechlinks, and grav engines deliberately use the corresponding loaded game or content-mod definition icon instead of custom replacements.

## Generation prompts

The source artwork was generated with OpenAI's built-in image generation tool and then cropped and downscaled for Unity UI use.

- Unknown research: fractured frontier compass surrounding a dark circular void, with a small question-shaped negative-space notch; graphite, aged brass, and a restrained cyan glint.
- Prerequisite: two compact square technology nodes offset diagonally and joined by a short cyan directional connector; square footprint and strong small-size silhouette.
- Research environment: a sturdy frontier laboratory workbench beneath a compact instrument lamp/scanner; graphite, aged brass, and restrained amber light.

All prompts required a genuinely transparent background, no text, no watermark, original artwork, and details broad enough to survive downscaling.
