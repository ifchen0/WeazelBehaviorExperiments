using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace WeazelBehaviorExperiments
{
    // Tag the lord a military aid permit creates for XCC, so the map component can watch it leave.
    [HarmonyPatch(typeof(RoyalTitlePermitWorker_CallAid), "CallAid")]
    public static class Patch_CallAid
    {
        public static void Prefix(Map map, Faction faction, out HashSet<Lord> __state)
        {
            __state = XccUtility.IsXcc(faction) ? new HashSet<Lord>(map.lordManager.lords) : null;
        }

        public static void Postfix(Map map, HashSet<Lord> __state)
        {
            if (__state != null) RegisterNewAidLords(map, __state);
        }

        public static void RegisterNewAidLords(Map map, HashSet<Lord> before)
        {
            var comp = MapComponent_WeazelExperiments.For(map);
            foreach (Lord lord in map.lordManager.lords.Where(l => !before.Contains(l) && l.LordJob is LordJob_AssistColony))
                comp?.RegisterAidLord(lord);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_PostApplyDamage
    {
        public static void Postfix(Pawn __instance, float totalDamageDealt)
        {
            if (totalDamageDealt <= 0f || !XccUtility.IsXcc(__instance.Faction)) return;
            Lord lord = __instance.GetLord();
            if (lord != null) MapComponent_WeazelExperiments.For(__instance.MapHeld)?.Notify_PawnDamaged(lord);
        }
    }
}
