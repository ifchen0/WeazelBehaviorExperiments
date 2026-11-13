using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace WeazelBehaviorExperiments
{
    // Royalty quests written for the Empire pick any faction with royal titles as the asker, so XCC can offer them too.
    // Vanilla then fills them with human pawns (the title holder, lodgers, mercenary helpers) and Empire shuttles,
    // because it has no Weazel pawn kinds for these slots. XCC is kept out of these quests' pawn selection.
    public static class EmpireQuests
    {
        private const string ExcludeVar = "wbeXccExcludedFactions";

        private static readonly HashSet<string> BlockedRoots = new HashSet<string>
        {
            "Hospitality_Joiners",
            "Hospitality_Prisoners",
            "Hospitality_Animals",
            "ThreatReward_Raid_MiscReward",
            "ThreatReward_MechPods_MiscReward",
            "ThreatReward_Infestation_ItemPod",
            "ThreatReward_Manhunters_ItemPod",
            "BuildMonument_Basic",
            "BuildMonument_TimeProtect",
            "PawnLend",
        };

        // QuestGen.Root is only set while a quest is generated; CanRun test runs do not set it.
        internal static QuestScriptDef testingRoot;

        private static bool Blocked()
        {
            if (!WeazelBehaviorExperimentsMod.Settings.noXccEmpireQuests) return false;
            QuestScriptDef root = testingRoot ?? QuestGen.Root;
            return root != null && BlockedRoots.Contains(root.defName);
        }

        // Points the node's excludeFactionDefs at a slate list that also holds XCC, so vanilla's own checks
        // skip XCC both for existing pawns and for the faction of a newly generated one.
        internal static object TryExcludeXcc(QuestNode_GetPawn node, Slate slate)
        {
            if (slate == null || !Blocked()) return null;
            FactionDef xcc = DefDatabase<FactionDef>.GetNamedSilentFail(XccUtility.XccFactionDefName);
            if (xcc == null) return null;
            List<FactionDef> current = node.excludeFactionDefs.GetValue(slate);
            if (current != null && current.Contains(xcc)) return null;
            var list = current == null ? new List<FactionDef>() : new List<FactionDef>(current);
            list.Add(xcc);
            slate.Set(ExcludeVar, list);
            object original = node.excludeFactionDefs;
            node.excludeFactionDefs = new SlateRef<List<FactionDef>>("$" + ExcludeVar);
            return original;
        }

        internal static void Restore(QuestNode_GetPawn node, object original)
        {
            if (original != null) node.excludeFactionDefs = (SlateRef<List<FactionDef>>)original;
        }
    }

    [HarmonyPatch(typeof(QuestScriptDef), nameof(QuestScriptDef.CanRun), typeof(Slate), typeof(IIncidentTarget))]
    public static class Patch_QuestCanRun
    {
        public static void Prefix(QuestScriptDef __instance, out QuestScriptDef __state)
        {
            __state = EmpireQuests.testingRoot;
            EmpireQuests.testingRoot = __instance;
        }

        public static void Finalizer(QuestScriptDef __state) => EmpireQuests.testingRoot = __state;
    }

    [HarmonyPatch(typeof(QuestNode_GetPawn), "TestRunInt")]
    public static class Patch_GetPawnTestRun
    {
        public static void Prefix(QuestNode_GetPawn __instance, Slate slate, out object __state) =>
            __state = EmpireQuests.TryExcludeXcc(__instance, slate);

        public static void Finalizer(QuestNode_GetPawn __instance, object __state) => EmpireQuests.Restore(__instance, __state);
    }

    [HarmonyPatch(typeof(QuestNode_GetPawn), "RunInt")]
    public static class Patch_GetPawnRun
    {
        public static void Prefix(QuestNode_GetPawn __instance, out object __state) =>
            __state = EmpireQuests.TryExcludeXcc(__instance, QuestGen.slate);

        public static void Finalizer(QuestNode_GetPawn __instance, object __state) => EmpireQuests.Restore(__instance, __state);
    }
}
