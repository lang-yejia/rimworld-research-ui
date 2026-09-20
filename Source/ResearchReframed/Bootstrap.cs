using HarmonyLib;
using Verse;

namespace ResearchReframed;

[StaticConstructorOnStartup]
internal static class Bootstrap
{
    static Bootstrap()
    {
        new Harmony("langyejia.researchreframed").PatchAll();
        Log.Message("[Research Reframed] Initialized.");
    }
}
