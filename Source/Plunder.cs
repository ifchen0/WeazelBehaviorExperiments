using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace WeazelBehaviorExperiments
{
    // Grab valuables, then leave. Each pawn takes the most valuable load it can carry within a wide radius
    // and walks off the map with it (vanilla Steal job), or simply leaves when nothing is worth taking.
    public class LordJob_WeazelPlunder : LordJob
    {
        // Invited squads know their way around the colony and open its doors; strike teams leave closed rooms alone.
        public bool throughDoors;
        // Drift looters: what they carry off settles goodwill once the last of them is gone.
        public bool settlesGoodwill;
        private float haul;

        public LordJob_WeazelPlunder()
        {
        }

        public LordJob_WeazelPlunder(bool throughDoors, bool settlesGoodwill = false)
        {
            this.throughDoors = throughDoors;
            this.settlesGoodwill = settlesGoodwill;
        }

        public override bool AddFleeToil => false;

        // Called before ExitMap destroys the carried load.
        public override void Notify_PawnLost(Pawn p, PawnLostCondition condition)
        {
            Thing load = p.carryTracker?.CarriedThing;
            if (settlesGoodwill && condition == PawnLostCondition.ExitedMap && load != null && !(load is Pawn))
                haul += load.MarketValue * load.stackCount;
        }

        public override void Cleanup()
        {
            if (settlesGoodwill) DriftPlunderUtility.Settle(lord.faction, haul);
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref throughDoors, "throughDoors");
            Scribe_Values.Look(ref settlesGoodwill, "settlesGoodwill");
            Scribe_Values.Look(ref haul, "haul");
        }

        public override StateGraph CreateGraph()
        {
            var graph = new StateGraph();
            var plunder = new LordToil_WeazelPlunder { useAvoidGrid = true };
            graph.AddToil(plunder);
            var assault = new LordToil_AssaultColony();
            graph.AddToil(assault);
            var turnHostile = new Transition(plunder, assault);
            turnHostile.AddTrigger(new Trigger_BecamePlayerEnemy());
            graph.AddTransition(turnHostile);
            return graph;
        }
    }

    public class LordToil_WeazelPlunder : LordToil
    {
        public override bool AllowSatisfyLongNeeds => false;

        // As in vanilla stealing: grab and go instead of stopping to bandage first.
        public override bool AllowSelfTend => false;

        public override void UpdateAllDuties()
        {
            foreach (Pawn pawn in lord.ownedPawns)
                pawn.mindState.duty = new PawnDuty(WBE_DefOf.WBE_Plunder);
        }
    }

    // Vanilla JobGiver_Steal only looks 12 cells (and 15 regions) around the thief and takes the nearest
    // good item. This one looks much further and takes whatever is worth the most in one load.
    public class JobGiver_WeazelSteal : ThinkNode_JobGiver
    {
        private const float SearchRadius = 45f;
        // Same floor as vanilla stealing: smaller loads are not worth the trip.
        private const float MinLoadValue = 320f;

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) || GenAI.InDangerousCombat(pawn))
                return null;
            if (!RCellFinder.TryFindBestExitSpot(pawn, out IntVec3 exit))
                return null;

            float capacity = pawn.GetStatValue(StatDefOf.CarryingCapacity);
            float radiusSquared = SearchRadius * SearchRadius;
            // As in vanilla stealing, items behind closed doors are off limits unless the lord allows doors,
            // so a closed storeroom protects its contents from strike teams.
            bool throughDoors = (pawn.GetLord()?.LordJob as LordJob_WeazelPlunder)?.throughDoors ?? false;
            TraverseParms traverse = throughDoors
                ? TraverseParms.For(pawn, Danger.Some)
                : TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Some);
            Thing best = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEverOrMinifiable)
                .Where(t => t.def.stealable && !t.IsBurning() && (t.Position - pawn.Position).LengthHorizontalSquared <= radiusSquared
                    && LoadValue(t, capacity) >= MinLoadValue)
                .OrderByDescending(t => LoadValue(t, capacity))
                .FirstOrDefault(t => pawn.CanReserve(t) && pawn.Map.reachability.CanReach(pawn.Position, t, PathEndMode.ClosestTouch, traverse));
            if (best == null) return null;

            Job job = JobMaker.MakeJob(JobDefOf.Steal, best, exit);
            job.count = LoadCount(best, capacity);
            return job;
        }

        private static int LoadCount(Thing t, float capacity) =>
            Mathf.Max(1, Mathf.Min(t.stackCount, (int)(capacity / t.def.VolumePerUnit)));

        private static float LoadValue(Thing t, float capacity) => t.MarketValue * LoadCount(t, capacity);
    }

    public static class PlunderUtility
    {
        // Moves the lord's pawns into a plunder lord. The old lord is removed once empty.
        public static void StartPlunder(Lord oldLord, string messageKey, bool throughDoors)
        {
            Map map = oldLord.Map;
            Faction faction = oldLord.faction;
            List<Pawn> pawns = oldLord.ownedPawns.Where(p => p.Spawned && !p.Dead).ToList();
            if (map == null || pawns.Count == 0) return;

            foreach (Pawn p in pawns)
                oldLord.RemovePawn(p);
            if (oldLord.ownedPawns.Count == 0)
                map.lordManager.RemoveLord(oldLord);

            LordMaker.MakeNewLord(faction, new LordJob_WeazelPlunder(throughDoors), map, pawns);
            Messages.Message(messageKey.Translate(faction.Named("FACTION")), new LookTargets(pawns), MessageTypeDefOf.NegativeEvent);
        }
    }
}
