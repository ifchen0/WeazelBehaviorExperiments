using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace WeazelBehaviorExperiments
{
    public class ExperimentSettings : ModSettings
    {
        // Chance that a caravan of an XCC enemy draws a strike team. 0 turns the behaviour off.
        public float caravanRaidChance = 0.3f;
        public bool aidPlunder = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref caravanRaidChance, "caravanRaidChance", 0.3f);
            Scribe_Values.Look(ref aidPlunder, "aidPlunder", true);
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
            listing.Gap();
            listing.CheckboxLabeled("WBE_AidPlunder".Translate(), ref Settings.aidPlunder, "WBE_AidPlunder_Desc".Translate());
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
    }
}
