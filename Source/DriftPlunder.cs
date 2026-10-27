using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace WeazelBehaviorExperiments
{
    // Every 50 days vanilla drifts XCC's goodwill 10 towards its natural -130 (Faction.CheckReachNaturalGoodwill).
    // When XCC is neutral but below 0 and the colony is worth it, XCC sends looters instead, and the haul buys goodwill.
    // Hooking the public TryAffectGoodwillWith rather than the private caller, which the runtime may inline.
    [HarmonyPatch(typeof(Faction), nameof(Faction.TryAffectGoodwillWith))]
    public static class Patch_NaturalGoodwillDrift
    {
        public static bool Prefix(Faction __instance, Faction other, int goodwillChange, HistoryEventDef reason, ref bool __result)
        {
            if (DriftPlunderUtility.applyingDrift || reason != HistoryEventDefOf.ReachNaturalGoodwill || goodwillChange >= 0
                || !XccUtility.IsXcc(__instance) || other == null || !other.IsPlayer)
                return true;
            if (!DriftPlunderUtility.TrySendLooters(__instance, out _)) return true;
            __result = false;
            return false;
        }
    }

    public static class DriftPlunderUtility
    {
        // Vanilla gift rate (FactionGiftUtility). Its falloff only starts above 0 goodwill, where the gain is capped.
        private const float SilverPerGoodwill = 40f;

        // Set while applying the vanilla drift ourselves, so the patch lets it through.
        internal static bool applyingDrift;

        // The richest player home map, or null.
        public static Map TargetMap() =>
            Find.Maps.Where(m => m.IsPlayerHome).MaxByWithFallback(m => m.wealthWatcher.WealthTotal);

        // Returns false (with the reason, for the debug action) when vanilla's drift should apply instead.
        public static bool TrySendLooters(Faction xcc, out string failReason)
        {
            failReason = null;
            Faction player = Faction.OfPlayer;
            Map map = TargetMap();
            if (!WeazelBehaviorExperimentsMod.Settings.driftPlunder)
                failReason = "turned off in settings";
            else if (!XccUtility.NeutralWithPlayer(xcc) || xcc.BaseGoodwillWith(player) >= 0)
                failReason = "XCC must be neutral with goodwill below 0";
            else if (map == null || map.wealthWatcher.WealthTotal < WeazelBehaviorExperimentsMod.Settings.driftPlunderMinWealth)
                failReason = "no player home map above the wealth setting";
            else if (!XccUtility.TrySpawnGroup(map, xcc, StorytellerUtility.DefaultThreatPointsNow(map), out var team))
                failReason = "could not spawn the squad";
            else
            {
                LordMaker.MakeNewLord(xcc, new LordJob_WeazelPlunder(throughDoors: true, settlesGoodwill: true), map, team);
                Messages.Message("WBE_DriftPlunderArrived".Translate(xcc.Named("FACTION")), new LookTargets(team), MessageTypeDefOf.NegativeEvent);
                return true;
            }
            return false;
        }

        // Goodwill for the haul, up to 0. Nothing taken: the vanilla drift it replaced.
        public static void Settle(Faction xcc, float haul)
        {
            Faction player = Faction.OfPlayer;
            if (xcc == null || xcc.defeated || xcc.HostileTo(player)) return;
            int gain = Mathf.FloorToInt(haul / SilverPerGoodwill);
            if (gain <= 0)
            {
                ApplyVanillaDrift(xcc);
                return;
            }
            // CalculateAdjustedGoodwillChange may scale the change, so shrink the request until it fits under 0.
            int room = -xcc.BaseGoodwillWith(player);
            while (gain > 0 && xcc.CalculateAdjustedGoodwillChange(player, gain) > room)
                gain--;
            if (gain > 0)
                xcc.TryAffectGoodwillWith(player, gain, canSendMessage: true, canSendHostilityLetter: false, reason: WBE_DefOf.WBE_DriftPlunder);
        }

        // Same step as Faction.CheckReachNaturalGoodwill: at most 10, down to natural goodwill + 50.
        private static void ApplyVanillaDrift(Faction xcc)
        {
            Faction player = Faction.OfPlayer;
            int change = Mathf.Min(10, xcc.BaseGoodwillWith(player) - (xcc.NaturalGoodwill + 50));
            if (change <= 0) return;
            applyingDrift = true;
            try
            {
                xcc.TryAffectGoodwillWith(player, -change, canSendMessage: true, canSendHostilityLetter: !xcc.temporary, reason: HistoryEventDefOf.ReachNaturalGoodwill);
            }
            finally
            {
                applyingDrift = false;
            }
        }
    }
}
