using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace WeazelBehaviorExperiments
{
    // XCC strike team: hunt the caravan's faction, then plunder once the caravan is gone or time runs out.
    public class LordJob_WeazelCaravanRaid : LordJob
    {
        private const int GiveUpTicks = 15000;

        private Faction targetFaction;
        private IntVec3 fallbackLocation;
        // The caravan's people. Pack animals and other pawns of the same faction do not count.
        private List<Pawn> caravanMembers = new List<Pawn>();

        public Faction TargetFaction => targetFaction;

        public LordJob_WeazelCaravanRaid()
        {
        }

        public LordJob_WeazelCaravanRaid(Faction targetFaction, IntVec3 fallbackLocation, IEnumerable<Pawn> caravanMembers)
        {
            this.targetFaction = targetFaction;
            this.fallbackLocation = fallbackLocation;
            this.caravanMembers = caravanMembers.ToList();
        }

        public override bool AddFleeToil => false;

        public override StateGraph CreateGraph()
        {
            var graph = new StateGraph();
            var hunt = new LordToil_HuntEnemies(fallbackLocation);
            graph.AddToil(hunt);
            var assault = new LordToil_AssaultColony();
            graph.AddToil(assault);

            var turnHostile = new Transition(hunt, assault);
            turnHostile.AddTrigger(new Trigger_BecamePlayerEnemy());
            graph.AddTransition(turnHostile);
            return graph;
        }

        // Plunder is a lord swap, so the map component does it outside the lord tick.
        public bool ShouldPlunder() =>
            lord.CurLordToil is LordToil_HuntEnemies && (lord.ticksInToil > GiveUpTicks || !CaravanStanding());

        // A caravan is done once none of its people is still holding its ground: downed, dead, gone,
        // fleeing in panic, cut off from the caravan, or leaving with it (any vanilla exit toil, including
        // LordToil_ExitMapTraderFighting, which shoots back on the way out).
        private bool CaravanStanding() => caravanMembers.Any(HoldingGround);

        private bool HoldingGround(Pawn p)
        {
            if (p == null || !p.Spawned || p.Map != lord.Map || p.Downed || p.Dead) return false;
            if (p.InMentalState && p.MentalStateDef == MentalStateDefOf.PanicFlee) return false;
            Lord caravanLord = p.GetLord();
            if (!(caravanLord?.LordJob is LordJob_TradeWithColony)) return false;
            LordToil toil = caravanLord.CurLordToil;
            return !(toil is LordToil_ExitMap || toil is LordToil_ExitMapAndEscortCarriers || toil is LordToil_ExitMapTraderFighting);
        }

        public override void ExposeData()
        {
            Scribe_References.Look(ref targetFaction, "targetFaction");
            Scribe_Values.Look(ref fallbackLocation, "fallbackLocation");
            Scribe_Collections.Look(ref caravanMembers, "caravanMembers", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                caravanMembers ??= new List<Pawn>();
                caravanMembers.RemoveAll(p => p == null);
            }
        }
    }

    public static class CaravanRaidUtility
    {
        public static bool Eligible(Lord caravanLord, Faction xcc)
        {
            Faction f = caravanLord.faction;
            return f != null && f != xcc && !f.IsPlayer && XccUtility.NeutralWithPlayer(xcc) && f.HostileTo(xcc)
                && !f.HostileTo(Faction.OfPlayer) && caravanLord.Map.IsPlayerHome;
        }

        public static bool TrySpawnStrikeTeam(Lord caravanLord)
        {
            Map map = caravanLord.Map;
            Faction xcc = XccUtility.Xcc;
            if (map == null || !Eligible(caravanLord, xcc)) return false;
            List<Pawn> caravan = caravanLord.ownedPawns.Where(p => p.Spawned && !p.Downed).ToList();
            if (caravan.Count == 0) return false;

            // Match the caravan's fighters, so the outcome is not decided before the shooting starts.
            float points = caravan.Where(p => p.RaceProps.Humanlike).Sum(p => p.kindDef.combatPower) * 1.2f;
            points = Mathf.Max(points, xcc.def.MinPointsToGeneratePawnGroup(PawnGroupKindDefOf.Combat));
            var parms = new PawnGroupMakerParms
            {
                groupKind = PawnGroupKindDefOf.Combat,
                tile = map.Tile,
                faction = xcc,
                points = points,
            };
            List<Pawn> team = PawnGroupMakerUtility.GeneratePawns(parms).ToList();
            if (team.Count == 0) return false;
            if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, map, CellFinder.EdgeRoadChance_Hostile))
            {
                foreach (Pawn p in team) Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.Discard);
                return false;
            }
            foreach (Pawn p in team)
                GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(entry, map, 8), map);

            LordMaker.MakeNewLord(xcc, new LordJob_WeazelCaravanRaid(caravanLord.faction, caravan[0].Position, caravan.Where(p => p.RaceProps.Humanlike)), map, team);
            Messages.Message("WBE_CaravanRaidArrived".Translate(xcc.Named("XCC"), caravanLord.faction.Named("CARAVAN")),
                new LookTargets(team), MessageTypeDefOf.ThreatSmall);
            return true;
        }
    }
}
