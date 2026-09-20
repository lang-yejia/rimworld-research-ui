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

    private static bool Prefix(Rect rect)
    {
        if (failed || Event.current.shift)
        {
            return true;
        }

        try
        {
            ResearchWindowRenderer.Draw(rect);
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
