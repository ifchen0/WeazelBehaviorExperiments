using System.Collections.Generic;
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

        // Same incident and parameters as the XCC_CallSquad permit (RoyalTitlePermitWorker_CallAid.CallAid),
        // without needing a titled caller. The squad is tracked like a permit call.
        [DebugAction(Category, "Call XCC aid squad (as permit)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CallAidSquad()
        {
            Map map = Find.CurrentMap;
            Faction xcc = XccUtility.Xcc;
            RoyalTitlePermitDef permit = DefDatabase<RoyalTitlePermitDef>.GetNamedSilentFail("XCC_CallSquad");
            if (xcc == null || xcc.HostileTo(Faction.OfPlayer) || permit?.royalAid == null)
            {
                Messages.Message("XCC must exist and not be hostile (and Royalty must be active).", MessageTypeDefOf.RejectInput, false);
                return;
            }
            var parms = new IncidentParms
            {
                target = map,
                faction = xcc,
                raidArrivalModeForQuickMilitaryAid = true,
                biocodeApparelChance = 1f,
                biocodeWeaponsChance = 1f,
                spawnCenter = DropCellFinder.TradeDropSpot(map),
            };
            if (permit.royalAid.pawnKindDef != null)
            {
                parms.pawnKind = permit.royalAid.pawnKindDef;
                parms.pawnCount = permit.royalAid.pawnCount;
            }
            else
                parms.points = permit.royalAid.points;

            // Registered by Patch_MilitaryAid, the same path a real permit or comms request takes.
            if (!IncidentDefOf.RaidFriendly.Worker.TryExecute(parms))
                Messages.Message("Could not send the aid squad.", MessageTypeDefOf.RejectInput, false);
        }

        // Skips the vanilla 25000-tick stay: runs the aid lord's own "time is up" transition (with its
        // exit-destination pre-action), exactly as when the timer fires.
        [DebugAction(Category, "XCC aid squads leave now", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void AidLeaveNow()
        {
            var comp = MapComponent_WeazelExperiments.For(Find.CurrentMap);
            foreach (Lord lord in Find.CurrentMap.lordManager.lords.Where(l => comp != null && comp.IsTrackedAidLord(l)).ToList())
            {
                Transition timeUp = lord.Graph.transitions.FirstOrDefault(t =>
                    t.sources.Contains(lord.CurLordToil) && t.triggers.Any(tr => tr is Trigger_TicksPassed));
                timeUp?.Execute(lord);
            }
        }

        // Sends the looters that replace the 50-day goodwill drift, under the same conditions.
        [DebugAction(Category, "XCC drift looters now", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DriftLootersNow()
        {
            if (!DriftPlunderUtility.TrySendLooters(XccUtility.Xcc, out string failReason))
                Messages.Message("Not sent: " + failReason + ".", MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction(Category, "XCC lords plunder now", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void PlunderNow()
        {
            foreach (Lord lord in Find.CurrentMap.lordManager.lords.Where(l => XccUtility.IsXcc(l.faction) && !(l.LordJob is LordJob_WeazelPlunder)).ToList())
                PlunderUtility.StartPlunder(lord, "WBE_AidPlunderMessage", throughDoors: true);
        }
    }
}
