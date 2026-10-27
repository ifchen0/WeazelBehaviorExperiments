using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace WeazelBehaviorExperiments
{
    public class ExperimentSettings : ModSettings
    {
        // Chance that a caravan of an XCC enemy draws a strike team. 0 turns the behaviour off.
        public float caravanRaidChance = 0.3f;
        public bool strikeTeamKidnap = true;
        public bool aidPlunder = true;
        public bool aidSalvage = true;
        // Goodwill lost when an XCC bestowing ceremony expires. 0 turns it off.
        public int bestowingPenalty = 15;
        public bool driftPlunder = true;
        // Colony wealth below this is not worth a visit. Default: vanilla's 14000, where wealth starts adding raid points.
        public int driftPlunderMinWealth = 14000;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref caravanRaidChance, "caravanRaidChance", 0.3f);
            Scribe_Values.Look(ref strikeTeamKidnap, "strikeTeamKidnap", true);
            Scribe_Values.Look(ref aidPlunder, "aidPlunder", true);
            Scribe_Values.Look(ref aidSalvage, "aidSalvage", true);
            Scribe_Values.Look(ref bestowingPenalty, "bestowingPenalty", 15);
            Scribe_Values.Look(ref driftPlunder, "driftPlunder", true);
            Scribe_Values.Look(ref driftPlunderMinWealth, "driftPlunderMinWealth", 14000);
        }
    }

    public class WeazelBehaviorExperimentsMod : Mod
    {
        public static ExperimentSettings Settings;

        public WeazelBehaviorExperimentsMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<ExperimentSettings>();
            new Harmony("ifchen0.weazelbehaviorexperiments").PatchAll();
        }

        public override string SettingsCategory() => "Weazel Behavior Experiments";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.Label("WBE_CaravanRaidChance".Translate(Settings.caravanRaidChance.ToStringPercent()), tooltip: "WBE_CaravanRaidChance_Desc".Translate());
            Settings.caravanRaidChance = Mathf.Round(listing.Slider(Settings.caravanRaidChance, 0f, 1f) * 20f) / 20f;
            listing.CheckboxLabeled("WBE_StrikeTeamKidnap".Translate(), ref Settings.strikeTeamKidnap, "WBE_StrikeTeamKidnap_Desc".Translate());
            listing.Gap();
            listing.CheckboxLabeled("WBE_AidPlunder".Translate(), ref Settings.aidPlunder, "WBE_AidPlunder_Desc".Translate());
            listing.CheckboxLabeled("WBE_AidSalvage".Translate(), ref Settings.aidSalvage, "WBE_AidSalvage_Desc".Translate());
            listing.Gap();
            listing.Label("WBE_BestowingPenalty".Translate(Settings.bestowingPenalty), tooltip: "WBE_BestowingPenalty_Desc".Translate());
            Settings.bestowingPenalty = Mathf.RoundToInt(listing.Slider(Settings.bestowingPenalty, 0f, 50f));
            listing.Gap();
            listing.CheckboxLabeled("WBE_DriftPlunder".Translate(), ref Settings.driftPlunder, "WBE_DriftPlunder_Desc".Translate());
            listing.Label("WBE_DriftPlunderMinWealth".Translate(((float)Settings.driftPlunderMinWealth).ToStringMoney()), tooltip: "WBE_DriftPlunderMinWealth_Desc".Translate());
            Settings.driftPlunderMinWealth = Mathf.RoundToInt(listing.Slider(Settings.driftPlunderMinWealth, 0f, 200000f) / 1000f) * 1000;
            listing.End();
        }
    }

    public static class XccUtility
    {
        public const string XccFactionDefName = "XCCRaidBase";

        public static Faction Xcc
        {
            get
            {
                FactionDef def = DefDatabase<FactionDef>.GetNamedSilentFail(XccFactionDefName);
                return def == null ? null : Find.FactionManager.FirstFactionOfDef(def);
            }
        }

        public static bool IsXcc(Faction f) => f?.def?.defName == XccFactionDefName;

        public static bool NeutralWithPlayer(Faction xcc) =>
            xcc != null && !xcc.defeated && xcc.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Neutral;

        // Spawns an XCC combat group at a map edge, walking in.
        public static bool TrySpawnGroup(Map map, Faction xcc, float points, out List<Pawn> team)
        {
            var parms = new PawnGroupMakerParms
            {
                groupKind = PawnGroupKindDefOf.Combat,
                tile = map.Tile,
                faction = xcc,
                points = Mathf.Max(points, xcc.def.MinPointsToGeneratePawnGroup(PawnGroupKindDefOf.Combat)),
            };
            team = PawnGroupMakerUtility.GeneratePawns(parms).ToList();
            return TrySpawnAtEdge(map, team);
        }

        // Spawns the squad an XCC_CallSquad permit sends: Weazel turns the placeholder kind into random real ones.
        public static bool TrySpawnAidSquad(Map map, Faction xcc, out List<Pawn> team)
        {
            const int SquadSize = 10;
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Weazel_Aid_Squad");
            if (kind == null) return TrySpawnGroup(map, xcc, 0f, out team);
            team = new List<Pawn>();
            for (int i = 0; i < SquadSize; i++)
                team.Add(PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, xcc, PawnGenerationContext.NonPlayer, tile: map.Tile)));
            return TrySpawnAtEdge(map, team);
        }

        private static bool TrySpawnAtEdge(Map map, List<Pawn> team)
        {
            if (team.Count == 0) return false;
            if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, map, CellFinder.EdgeRoadChance_Hostile))
            {
                foreach (Pawn p in team) Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.Discard);
                return false;
            }
            foreach (Pawn p in team)
                GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(entry, map, 8), map);
            return true;
        }
    }
}
