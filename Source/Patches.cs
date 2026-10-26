using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace WeazelBehaviorExperiments
{
    // Tag the lord of military aid XCC sends on request, so the map component can watch it leave.
    // Permits (RoyalTitlePermitWorker_CallAid.CallAid) and the comms console (FactionDialogMaker.CallForAid)
    // both end in RaidFriendly.Worker.TryExecute with raidArrivalModeForQuickMilitaryAid set. Hooking that
    // shared entry instead of the two small callers, which the 1.7 runtime may inline past a patch.
    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
    public static class Patch_MilitaryAid
    {
        public static void Prefix(IncidentParms parms, out HashSet<Lord> __state)
        {
            __state = parms.raidArrivalModeForQuickMilitaryAid && XccUtility.IsXcc(parms.faction) && parms.target is Map map
                ? new HashSet<Lord>(map.lordManager.lords)
                : null;
        }

        public static void Postfix(IncidentParms parms, bool __result, HashSet<Lord> __state)
        {
            if (!__result || __state == null || !(parms.target is Map map)) return;
            var comp = MapComponent_WeazelExperiments.For(map);
            foreach (Lord lord in map.lordManager.lords.Where(l => !__state.Contains(l) && l.LordJob is LordJob_AssistColony))
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
