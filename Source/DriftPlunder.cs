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
        // Vanilla gift rate (FactionGiftUtility.GetBaseGoodwillChange).
        private const float SilverPerGoodwill = 40f;

        // Vanilla gift falloff: full rate up to 0 goodwill, a quarter at 75, at most 200 per gift.
        private static readonly System.Func<float, Faction, int> GiftFalloff =
            AccessTools.MethodDelegate<System.Func<float, Faction, int>>(
                AccessTools.Method(typeof(RimWorld.Planet.FactionGiftUtility), "PostProcessedGoodwillChange"));

        // The haul can lift XCC to ally, no further: beyond that the colony would only be losing goods.
        private const int GoodwillCeiling = 75;

        // Silver needed to lift goodwill to the ceiling under the gift falloff, stepped as vanilla does.
        // One extra step covers vanilla rounding down; Settle trims any overshoot.
        private static float BudgetToCeiling(int goodwill)
        {
            SimpleCurve curve = DiplomacyTuning.GiftGoodwillFactorRelationsCurve;
            float reached = goodwill, baseChange = 0.25f;
            while (reached < GoodwillCeiling)
            {
                reached += 0.25f * curve.Evaluate(Mathf.Min(reached, 100f));
                baseChange += 0.25f;
            }
            return baseChange * SilverPerGoodwill;
        }

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
            else if (!XccUtility.TrySpawnAidSquad(map, xcc, out var team))
                failReason = "could not spawn the squad";
            else
            {
                // Walk in to where caravans unload before looting: from the map edge nothing is in reach.
                var job = new LordJob_WeazelPlunder(throughDoors: true, settlesGoodwill: true)
                {
                    travelTo = DropCellFinder.TradeDropSpot(map),
                    budget = BudgetToCeiling(xcc.BaseGoodwillWith(player)),
                };
                LordMaker.MakeNewLord(xcc, job, map, team);
                Messages.Message("WBE_DriftPlunderArrived".Translate(xcc.Named("FACTION")), new LookTargets(team), MessageTypeDefOf.NegativeEvent);
                return true;
            }
            return false;
        }

        // Goodwill for the haul, as if it were a gift, up to the ceiling. Nothing taken: the vanilla drift it replaced.
        public static void Settle(Faction xcc, float haul)
        {
            Faction player = Faction.OfPlayer;
            if (xcc == null || xcc.defeated || xcc.HostileTo(player)) return;
            int gain = GiftFalloff(haul / SilverPerGoodwill, xcc);
            if (gain <= 0)
            {
                ApplyVanillaDrift(xcc);
                return;
            }
            // CalculateAdjustedGoodwillChange may scale the change, so shrink the request until it fits.
            int room = GoodwillCeiling - xcc.BaseGoodwillWith(player);
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
