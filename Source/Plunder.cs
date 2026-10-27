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
        // Set for squads arriving at the map edge: walk here first, then loot.
        public IntVec3 travelTo = IntVec3.Invalid;
        // Market value the whole squad may take. Negative: no limit.
        public float budget = -1f;
        // Squads that fought for the colony take only weapons and apparel left lying outside storage.
        public bool battlefieldOnly;
        // Strike teams carry these off (once downed) before grabbing goods: the caravan's own people.
        public List<Pawn> kidnapTargets = new List<Pawn>();
        private float haul;

        // Stragglers that cannot reach the spot should not hold the others back forever.
        private const int TravelGiveUpTicks = 5000;

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
            if (condition == PawnLostCondition.ExitedMap && load != null && !(load is Pawn))
                haul += load.MarketValue * load.stackCount;
        }

        // Budget left after what is already gone, being carried, or picked by another member on the way to it.
        // Recounted each time, so a load dropped by a downed member frees its share again.
        public float RemainingBudget(Pawn asking)
        {
            if (budget < 0f) return float.PositiveInfinity;
            float committed = haul;
            foreach (Pawn p in lord.ownedPawns)
            {
                Thing carried = p.carryTracker?.CarriedThing;
                if (carried != null)
                {
                    if (!(carried is Pawn)) committed += carried.MarketValue * carried.stackCount;
                }
                else if (p != asking && p.CurJobDef == JobDefOf.Steal && p.CurJob.targetA.Thing is Thing target)
                    committed += target.MarketValue * Mathf.Min(p.CurJob.count, target.stackCount);
            }
            return budget - committed;
        }

        public override void Cleanup()
        {
            if (settlesGoodwill) DriftPlunderUtility.Settle(lord.faction, haul);
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref throughDoors, "throughDoors");
            Scribe_Values.Look(ref settlesGoodwill, "settlesGoodwill");
            Scribe_Values.Look(ref travelTo, "travelTo", IntVec3.Invalid);
            Scribe_Values.Look(ref budget, "budget", -1f);
            Scribe_Values.Look(ref haul, "haul");
            Scribe_Values.Look(ref battlefieldOnly, "battlefieldOnly");
            Scribe_Collections.Look(ref kidnapTargets, "kidnapTargets", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                kidnapTargets ??= new List<Pawn>();
                kidnapTargets.RemoveAll(p => p == null);
            }
        }

        // Kidnap targets the given pawn could carry off right now (vanilla Kidnap job fails once they get up).
        public bool CanKidnap(Pawn kidnapper, Pawn victim) =>
            victim.Spawned && victim.Map == kidnapper.Map && victim.Downed && !victim.Dead && !victim.IsPrisoner
            && !victim.InBed() && victim.Faction != null && victim.Faction.HostileTo(kidnapper.Faction);

        public override StateGraph CreateGraph()
        {
            var graph = new StateGraph();
            LordToil_Travel travel = null;
            if (travelTo.IsValid)
            {
                travel = new LordToil_Travel(travelTo) { useAvoidGrid = true };
                graph.AddToil(travel);
            }
            var plunder = new LordToil_WeazelPlunder { useAvoidGrid = true };
            graph.AddToil(plunder);
            var assault = new LordToil_AssaultColony();
            graph.AddToil(assault);

            var turnHostile = new Transition(plunder, assault);
            turnHostile.AddTrigger(new Trigger_BecamePlayerEnemy());
            if (travel != null)
            {
                turnHostile.AddSource(travel);
                var arrived = new Transition(travel, plunder);
                arrived.AddTrigger(new Trigger_Memo("TravelArrived"));
                arrived.AddTrigger(new Trigger_TicksPassed(TravelGiveUpTicks));
                graph.AddTransition(arrived);
            }
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
        public const float SearchRadius = 45f;
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
            var plunderJob = pawn.GetLord()?.LordJob as LordJob_WeazelPlunder;
            bool throughDoors = plunderJob?.throughDoors ?? false;
            bool battlefieldOnly = plunderJob?.battlefieldOnly ?? false;
            // A squad with a budget takes only part of a stack that would overrun it, and skips single items that would.
            float budget = plunderJob?.RemainingBudget(pawn) ?? float.PositiveInfinity;
            if (budget < MinLoadValue) return null;
            TraverseParms traverse = throughDoors
                ? TraverseParms.For(pawn, Danger.Some)
                : TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Some);
            Thing best = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEverOrMinifiable)
                .Where(t => t.def.stealable && !t.IsBurning() && (t.Position - pawn.Position).LengthHorizontalSquared <= radiusSquared
                    && (!battlefieldOnly || (t.def.IsWeapon || t.def.IsApparel) && !t.IsInAnyStorage())
                    && LoadValue(t, capacity, budget) >= MinLoadValue)
                .OrderByDescending(t => LoadValue(t, capacity, budget))
                .FirstOrDefault(t => pawn.CanReserve(t) && pawn.Map.reachability.CanReach(pawn.Position, t, PathEndMode.ClosestTouch, traverse));
            if (best == null) return null;

            Job job = JobMaker.MakeJob(JobDefOf.Steal, best, exit);
            job.count = LoadCount(best, capacity, budget);
            return job;
        }

        // 0 when not even one unit fits the budget.
        private static int LoadCount(Thing t, float capacity, float budget)
        {
            int count = Mathf.Max(1, Mathf.Min(t.stackCount, (int)(capacity / t.def.VolumePerUnit)));
            float unitValue = t.MarketValue;
            if (!float.IsPositiveInfinity(budget) && unitValue > 0f)
                count = Mathf.Min(count, (int)(budget / unitValue));
            return count;
        }

        private static float LoadValue(Thing t, float capacity, float budget) => t.MarketValue * LoadCount(t, capacity, budget);
    }

    // Vanilla JobGiver_Kidnap only takes downed colonists. This one takes the plunder lord's own targets.
    public class JobGiver_WeazelKidnap : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var plunderJob = pawn.GetLord()?.LordJob as LordJob_WeazelPlunder;
            if (plunderJob == null || plunderJob.kidnapTargets.Count == 0) return null;
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) || GenAI.InDangerousCombat(pawn))
                return null;
            if (!RCellFinder.TryFindBestExitSpot(pawn, out IntVec3 exit))
                return null;

            float radiusSquared = JobGiver_WeazelSteal.SearchRadius * JobGiver_WeazelSteal.SearchRadius;
            TraverseParms traverse = plunderJob.throughDoors
                ? TraverseParms.For(pawn, Danger.Some)
                : TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Some);
            Pawn victim = plunderJob.kidnapTargets
                .Where(v => plunderJob.CanKidnap(pawn, v) && (v.Position - pawn.Position).LengthHorizontalSquared <= radiusSquared)
                .OrderBy(v => (v.Position - pawn.Position).LengthHorizontalSquared)
                .FirstOrDefault(v => pawn.CanReserve(v) && pawn.Map.reachability.CanReach(pawn.Position, v, PathEndMode.OnCell, traverse));
            if (victim == null) return null;

            Job job = JobMaker.MakeJob(JobDefOf.Kidnap, victim, exit);
            job.count = 1;
            return job;
        }
    }

    public static class PlunderUtility
    {
        // Moves the lord's pawns into a plunder lord. The old lord is removed once empty.
        public static void StartPlunder(Lord oldLord, string messageKey, bool throughDoors) =>
            StartPlunder(oldLord, messageKey, new LordJob_WeazelPlunder(throughDoors));

        public static void StartPlunder(Lord oldLord, string messageKey, LordJob_WeazelPlunder plunderJob)
        {
            Map map = oldLord.Map;
            Faction faction = oldLord.faction;
            List<Pawn> pawns = oldLord.ownedPawns.Where(p => p.Spawned && !p.Dead).ToList();
            if (map == null || pawns.Count == 0) return;

            foreach (Pawn p in pawns)
                oldLord.RemovePawn(p);
            if (oldLord.ownedPawns.Count == 0)
                map.lordManager.RemoveLord(oldLord);

            LordMaker.MakeNewLord(faction, plunderJob, map, pawns);
            Messages.Message(messageKey.Translate(faction.Named("FACTION")), new LookTargets(pawns), MessageTypeDefOf.NegativeEvent);
        }
    }
}
