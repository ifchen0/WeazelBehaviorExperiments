using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace WeazelBehaviorExperiments
{
    public static class DebugActions
    {
        private const string Category = "Weazel Behavior Experiments";

        [DebugAction(Category, "Strike team on caravan now", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StrikeTeamNow()
        {
            Lord caravan = Find.CurrentMap.lordManager.lords.FirstOrDefault(l => l.LordJob is LordJob_TradeWithColony);
            if (caravan == null)
                Messages.Message("No trade caravan on this map.", MessageTypeDefOf.RejectInput, false);
            else if (!CaravanRaidUtility.TrySpawnStrikeTeam(caravan))
                Messages.Message("Not eligible: XCC must be neutral with the player and hostile to the caravan's faction.", MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction(Category, "XCC lords plunder now", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void PlunderNow()
        {
            foreach (Lord lord in Find.CurrentMap.lordManager.lords.Where(l => XccUtility.IsXcc(l.faction) && !(l.LordJob is LordJob_WeazelPlunder)).ToList())
                PlunderUtility.StartPlunder(lord, "WBE_AidPlunderMessage", throughDoors: true);
        }
    }
}
