using System.Reflection;
using HarmonyLib;
using Verse;

namespace WeazelBehaviorExperiments
{
    // Weazel Mode's WorkGiver_FillWeazelCloneVat asks for every artificial building on the map
    // (ThingRequestGroup.BuildingArtificial) and has no ShouldSkip, so each hauler's job scan floods all
    // reachable regions checking walls, shelves and doors - 15-40 ms per scan on a large base, even with no
    // clone vat built. Only Building_WeazelCloneVat passes its HasJobOnThing, so requesting the vat def alone
    // gives the same candidates and costs nothing when no vat exists.
    // Temporary workaround until the scan is narrowed upstream; it switches itself off if the class is gone.
    [HarmonyPatch]
    public static class Patch_CloneVatScan
    {
        private static ThingDef vatDef;

        public static bool Prepare() => TargetMethod() != null;

        public static MethodBase TargetMethod()
        {
            var type = GenTypes.GetTypeInAnyAssembly("WeazelClone.WorkGiver_FillWeazelCloneVat");
            return type == null ? null : AccessTools.DeclaredPropertyGetter(type, "PotentialWorkThingRequest");
        }

        public static bool Prefix(ref ThingRequest __result)
        {
            vatDef ??= DefDatabase<ThingDef>.GetNamedSilentFail("Weazel_CloneVat");
            if (vatDef == null) return true;
            __result = ThingRequest.ForDef(vatDef);
            return false;
        }
    }
}
