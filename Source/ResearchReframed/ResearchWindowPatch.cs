using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ResearchReframed;

[HarmonyPatch(typeof(MainTabWindow_Research), nameof(MainTabWindow_Research.DoWindowContents))]
internal static class ResearchWindowPatch
{
    private static bool failed;

    private static bool Prefix(Rect __0)
    {
        if (failed || Event.current.shift)
        {
            return true;
        }

        try
        {
            ResearchWindowRenderer.Draw(__0);
            return false;
        }
        catch (Exception exception)
        {
            failed = true;
            Log.Error($"[Research Reframed] UI failed; falling back to vanilla.\n{exception}");
            Messages.Message("RR_FallbackWarning".Translate(), MessageTypeDefOf.RejectInput, false);
            return true;
        }
    }
}

[HarmonyPatch(typeof(MainTabWindow_Research), nameof(MainTabWindow_Research.InitialSize), MethodType.Getter)]
internal static class ResearchWindowSizePatch
{
    private static void Postfix(ref Vector2 __result)
    {
        __result = new Vector2(UI.screenWidth, Mathf.Max(700f, UI.screenHeight - 35f));
    }
}
