using HarmonyLib;
using RimWorld;
using System.Linq;
using Verse;
using Verse.AI;

namespace WeazelBehaviorExperiments
{
    // XCC blames the colony for any failed bestowing ceremony, whatever went wrong, and takes goodwill for it,
    // but never enough to turn hostile while its officer is still on the map.
    [HarmonyPatch(typeof(Quest), nameof(Quest.End))]
    public static class Patch_BestowingFailed
    {
        // Goodwill at or below -75 turns a faction hostile.
        private const int LowestNonHostileGoodwill = -74;

        // Only accepted ceremonies count: before that the officer has not even arrived.
        public static void Prefix(Quest __instance, out bool __state) => __state = __instance.State == QuestState.Ongoing;

        public static void Postfix(Quest __instance, QuestEndOutcome outcome, bool __state)
        {
            if (!__state || outcome != QuestEndOutcome.Fail) return;
            var ceremony = __instance.PartsListForReading.OfType<QuestPart_BestowingCeremony>().FirstOrDefault();
            Faction xcc = ceremony?.bestower?.Faction ?? ceremony?.faction;
            if (!XccUtility.IsXcc(xcc)) return;

            int penalty = WeazelBehaviorExperimentsMod.Settings.bestowingPenalty;
            Faction player = Faction.OfPlayer;
            // Already hostile also covers a destroyed shuttle, which vanilla punishes with war.
            if (penalty <= 0 || xcc.HostileTo(player)) return;

            // Vanilla scales a drop towards natural goodwill up by as much as 25%, so shrink the request
            // until the adjusted change stays above the hostility threshold.
            int room = xcc.BaseGoodwillWith(player) - LowestNonHostileGoodwill;
            int change = -penalty;
            while (change < 0 && -xcc.CalculateAdjustedGoodwillChange(player, change) > room)
                change++;
            if (change < 0)
                xcc.TryAffectGoodwillWith(player, change, canSendMessage: true, canSendHostilityLetter: false, reason: WBE_DefOf.WBE_BestowingFailed);
        }
    }

    [DefOf]
    public static class WBE_DefOf
    {
        public static HistoryEventDef WBE_BestowingFailed;
        public static DutyDef WBE_Plunder;

        static WBE_DefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(WBE_DefOf));
    }
}
