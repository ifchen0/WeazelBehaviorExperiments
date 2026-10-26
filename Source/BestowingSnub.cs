using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI.Group;

namespace WeazelBehaviorExperiments
{
    // Letting an XCC bestowing ceremony expire is taken as a snub and costs goodwill,
    // but never enough to turn XCC hostile while its officer is still on the map.
    [HarmonyPatch(typeof(QuestUtility), nameof(QuestUtility.SendQuestTargetSignals),
        new[] { typeof(List<string>), typeof(string), typeof(NamedArgument) })]
    public static class Patch_BestowingExpired
    {
        // Goodwill at or below -75 turns a faction hostile.
        private const int LowestNonHostileGoodwill = -74;

        public static void Postfix(string signalPart, NamedArgument arg1)
        {
            if (signalPart != "CeremonyExpired" || !(arg1.arg is Lord lord) || !XccUtility.IsXcc(lord.faction)) return;
            int penalty = WeazelBehaviorExperimentsMod.Settings.bestowingPenalty;
            Faction xcc = lord.faction;
            Faction player = Faction.OfPlayer;
            if (penalty <= 0 || xcc.HostileTo(player)) return;

            // Vanilla scales a drop towards natural goodwill up by as much as 25%, so shrink the request
            // until the adjusted change stays above the hostility threshold.
            int room = xcc.BaseGoodwillWith(player) - LowestNonHostileGoodwill;
            int change = -penalty;
            while (change < 0 && -xcc.CalculateAdjustedGoodwillChange(player, change) > room)
                change++;
            if (change < 0)
                xcc.TryAffectGoodwillWith(player, change, canSendMessage: true, canSendHostilityLetter: false, reason: WBE_DefOf.WBE_BestowingExpired);
        }
    }

    [DefOf]
    public static class WBE_DefOf
    {
        public static HistoryEventDef WBE_BestowingExpired;

        static WBE_DefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(WBE_DefOf));
    }
}
