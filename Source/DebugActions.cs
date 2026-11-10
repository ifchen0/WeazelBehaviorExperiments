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

        // Puts the clicked pawn on XCC's kidnapped list as a real kidnapping would, minus the letter
        // (vanilla only sends it when there is a kidnapper).
        [DebugAction(Category, "XCC kidnaps this pawn", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void XccKidnaps(Pawn p)
        {
            Faction xcc = XccUtility.Xcc;
            // Same victims vanilla kidnappers accept (KidnapAIUtility): no subhumans such as ghouls.
            if (xcc == null || !p.RaceProps.Humanlike || p.IsSubhuman || p.Faction == xcc || xcc.kidnapped.KidnappedPawnsListForReading.Contains(p))
            {
                Messages.Message("Needs a humanlike, non-subhuman pawn that is not XCC's and not already kidnapped.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            xcc.kidnapped.Kidnap(p, null);
            Messages.Message($"{p.LabelShort} kidnapped by XCC ({xcc.kidnapped.KidnappedPawnsListForReading.Count} captives).", MessageTypeDefOf.NeutralEvent, false);
        }

        // Skips the wait after a comms request: an XCC slave merchant caravan walks in right away.
        [DebugAction(Category, "XCC slave merchant caravan now", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SlaveCaravanNow()
        {
            Faction xcc = XccUtility.Xcc;
            TraderKindDef kind = DefDatabase<TraderKindDef>.GetNamedSilentFail("Caravan_Weazel_SlaveMerchant");
            if (xcc == null || kind == null || xcc.HostileTo(Faction.OfPlayer))
            {
                Messages.Message("XCC must exist and not be hostile (and Ideology must be active).", MessageTypeDefOf.RejectInput, false);
                return;
            }
            var parms = new IncidentParms { target = Find.CurrentMap, faction = xcc, traderKind = kind };
            if (!IncidentDefOf.TraderCaravanArrival.Worker.TryExecute(parms))
                Messages.Message("Could not send the caravan.", MessageTypeDefOf.RejectInput, false);
        }

        // An XCC slave merchant ship in orbit, as vanilla's orbital trader arrival makes it (no letter).
        [DebugAction(Category, "XCC slave trade ship now", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SlaveShipNow()
        {
            Faction xcc = XccUtility.Xcc;
            TraderKindDef kind = DefDatabase<TraderKindDef>.GetNamedSilentFail("Orbital_Weazel_SlaveMerchant");
            if (xcc == null || kind == null)
            {
                Messages.Message("XCC must exist (and Ideology must be active).", MessageTypeDefOf.RejectInput, false);
                return;
            }
            var ship = new TradeShip(kind, xcc);
            Find.CurrentMap.passingShipManager.AddShip(ship);
            ship.GenerateThings();
            Messages.Message($"{ship.TraderName} is in orbit; trade through a comms console.", MessageTypeDefOf.NeutralEvent, false);
        }

        // Skips the ~30-day roll: every current XCC captive is put up for sale. Request an XCC slave
        // merchant afterwards to see them in its stock.
        [DebugAction(Category, "Put XCC captives up for sale now", allowedGameStates = AllowedGameStates.Playing)]
        private static void CaptivesForSaleNow()
        {
            Faction xcc = XccUtility.Xcc;
            if (xcc == null || GameComponent_XccSlaves.Instance == null) return;
            GameComponent_XccSlaves.Instance.MarkAllForSale(xcc);
            Messages.Message("XCC captives up for sale: " + xcc.kidnapped.KidnappedPawnsListForReading.Count + ".", MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction(Category, "XCC lords plunder now", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void PlunderNow()
        {
            foreach (Lord lord in Find.CurrentMap.lordManager.lords.Where(l => XccUtility.IsXcc(l.faction) && !(l.LordJob is LordJob_WeazelPlunder)).ToList())
            {
                if (lord.LordJob is LordJob_WeazelCaravanRaid)
                    CaravanRaidUtility.StartPlunder(lord);
                else
                    PlunderUtility.StartPlunder(lord, "WBE_AidPlunderMessage", new LordJob_WeazelPlunder(throughDoors: true) { silverOnly = true });
            }
        }
    }
}
