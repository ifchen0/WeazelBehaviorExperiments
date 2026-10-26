using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace WeazelBehaviorExperiments
{
    // Grab valuables nearby, then leave. Uses the vanilla Steal duty: each pawn takes one stack within
    // 12 cells (worth 320+) and carries it off the map, or simply leaves when nothing is in reach.
    public class LordJob_WeazelPlunder : LordJob
    {
        public override bool AddFleeToil => false;

        public override StateGraph CreateGraph()
        {
            var graph = new StateGraph();
            var plunder = new LordToil_StealCover { cover = false, useAvoidGrid = true };
            graph.AddToil(plunder);
            var assault = new LordToil_AssaultColony();
            graph.AddToil(assault);
            var turnHostile = new Transition(plunder, assault);
            turnHostile.AddTrigger(new Trigger_BecamePlayerEnemy());
            graph.AddTransition(turnHostile);
            return graph;
        }
    }

    public static class PlunderUtility
    {
        // Moves the lord's pawns into a plunder lord. The old lord is removed once empty.
        public static void StartPlunder(Lord oldLord, string messageKey)
        {
            Map map = oldLord.Map;
            Faction faction = oldLord.faction;
            List<Pawn> pawns = oldLord.ownedPawns.Where(p => p.Spawned && !p.Dead).ToList();
            if (map == null || pawns.Count == 0) return;

            foreach (Pawn p in pawns)
                oldLord.RemovePawn(p);
            if (oldLord.ownedPawns.Count == 0)
                map.lordManager.RemoveLord(oldLord);

            LordMaker.MakeNewLord(faction, new LordJob_WeazelPlunder(), map, pawns);
            Messages.Message(messageKey.Translate(faction.Named("FACTION")), new LookTargets(pawns), MessageTypeDefOf.NegativeEvent);
        }
    }
}
