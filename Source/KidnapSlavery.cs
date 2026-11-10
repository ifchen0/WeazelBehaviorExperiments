using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace WeazelBehaviorExperiments
{
    // XCC does not take the people it carries off into its ranks. They stay captives (colonists can still be
    // ransomed), and where vanilla would recruit them XCC puts them up for sale instead, so its slave
    // merchants may bring them along. Colonists nobody buys go back into captivity; anyone else is gone.
    public class GameComponent_XccSlaves : GameComponent
    {
        // Same interval and odds as vanilla's recruit roll in KidnappedPawnsTracker.
        private const int CheckInterval = 15051;
        private const float ForSaleMtbDays = 30f;
        // Weazel's slave merchants carry 5 slaves; captives replace at most this many of them.
        private const int MaxPerTrader = 2;

        // Captives up for sale. They stay on XCC's kidnapped list, which keeps them from the world pawn GC
        // and keeps colonists eligible for vanilla ransom demands.
        private List<Pawn> forSale = new List<Pawn>();
        // Former colonists placed in a trader's stock, watched so they return to captivity if unsold.
        private List<Pawn> onSale = new List<Pawn>();
        // Their own pawn kind, swapped for Slave while on sale (caravans only offer Slave-kind pawns).
        private Dictionary<Pawn, PawnKindDef> ownKinds = new Dictionary<Pawn, PawnKindDef>();
        private List<Pawn> ownKindKeys;
        private List<PawnKindDef> ownKindValues;
        // Outsiders sold as slaves and the faction they were taken from, which becomes their home once bought,
        // so emancipating them sends them back there instead of to XCC.
        private Dictionary<Pawn, Faction> homeFactions = new Dictionary<Pawn, Faction>();
        private List<Pawn> homeFactionKeys;
        private List<Faction> homeFactionValues;

        private static readonly AccessTools.FieldRef<Pawn_GuestTracker, Faction> SlaveFactionRef =
            AccessTools.FieldRefAccess<Pawn_GuestTracker, Faction>("slaveFactionInt");

        public GameComponent_XccSlaves(Game game) { }

        public static GameComponent_XccSlaves Instance => Current.Game?.GetComponent<GameComponent_XccSlaves>();

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref forSale, "forSale", LookMode.Reference);
            Scribe_Collections.Look(ref onSale, "onSale", LookMode.Reference);
            Scribe_Collections.Look(ref ownKinds, "ownKinds", LookMode.Reference, LookMode.Def, ref ownKindKeys, ref ownKindValues);
            Scribe_Collections.Look(ref homeFactions, "homeFactions", LookMode.Reference, LookMode.Reference, ref homeFactionKeys, ref homeFactionValues);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                forSale = forSale ?? new List<Pawn>();
                onSale = onSale ?? new List<Pawn>();
                ownKinds = ownKinds ?? new Dictionary<Pawn, PawnKindDef>();
                homeFactions = homeFactions ?? new Dictionary<Pawn, Faction>();
                homeFactions.RemoveAll(kv => kv.Key == null || kv.Value == null);
                forSale.RemoveAll(p => p == null);
                onSale.RemoveAll(p => p == null);
                ownKinds.RemoveAll(kv => kv.Key == null || kv.Value == null);
            }
        }

        // Replaces vanilla's tracker tick for XCC: same cleanup, but the recruit roll marks the captive for sale.
        public void CaptivesTick(Faction xcc, List<Pawn> captives)
        {
            captives.RemoveAll(p => p.DestroyedOrNull());
            if (Find.TickManager.TicksGame % CheckInterval != 0) return;
            ReturnUnsold(xcc, captives);
            forSale.RemoveAll(p => p.DestroyedOrNull() || !captives.Contains(p));
            // Unsold outsiders end up discarded by the world pawn GC.
            homeFactions.RemoveAll(kv => kv.Key.DestroyedOrNull() || kv.Key.Discarded || kv.Key.Dead);
            // Without Ideology there are no slave merchants, so captives just stay captive.
            if (!ModsConfig.IdeologyActive) return;
            foreach (Pawn p in captives)
                if (!forSale.Contains(p) && Rand.MTBEventOccurs(ForSaleMtbDays, GenDate.TicksPerDay, CheckInterval))
                    MarkForSale(xcc, p);
        }

        public void MarkForSale(Faction xcc, Pawn p)
        {
            if (forSale.Contains(p)) return;
            forSale.Add(p);
            if (p.Faction == Faction.OfPlayer)
                Messages.Message("WBE_CaptiveForSale".Translate(p.Named("PAWN"), xcc.Named("FACTION")), MessageTypeDefOf.NegativeEvent);
        }

        public void MarkAllForSale(Faction xcc)
        {
            foreach (Pawn p in xcc.kidnapped.KidnappedPawnsListForReading.Where(p => !p.DestroyedOrNull()).ToList())
                MarkForSale(xcc, p);
        }

        // Former colonists the trader left with come back to the world as XCC pawns: put them back into captivity.
        public void ReturnUnsold(Faction xcc, List<Pawn> captives)
        {
            for (int i = onSale.Count - 1; i >= 0; i--)
            {
                Pawn p = onSale[i];
                // Bought, freed, captured or dead: no longer XCC's to sell.
                if (p.DestroyedOrNull() || p.Dead || p.Faction != xcc || p.HostFaction != null)
                {
                    onSale.RemoveAt(i);
                    if (!p.DestroyedOrNull() && p.Faction == Faction.OfPlayer) RestoreOwnKind(p);
                    else ownKinds.Remove(p);
                    continue;
                }
                if (!Find.WorldPawns.Contains(p))
                {
                    // Still in a caravan on a map or in a trade ship's hold.
                    if (p.Spawned || p.ParentHolder != null) continue;
                    // Stock that was generated but never used.
                    Find.WorldPawns.PassToWorld(p);
                }
                else if (p.IsCaravanMember())
                {
                    continue;
                }
                onSale.RemoveAt(i);
                // Back on the list before the faction change, so the world pawn never looks like a free colonist.
                captives.Add(p);
                p.SetFaction(Faction.OfPlayer);
                RestoreOwnKind(p);
                if (!forSale.Contains(p)) forSale.Add(p);
            }
        }

        // Swaps some freshly generated slaves of an XCC slave merchant for captives up for sale.
        public List<Thing> FillStock(List<Thing> stock)
        {
            Faction xcc = XccUtility.Xcc;
            if (xcc == null || forSale.Count == 0) return stock;
            List<Pawn> captives = xcc.kidnapped.KidnappedPawnsListForReading;
            List<Pawn> picks = forSale
                .Where(p => !p.DestroyedOrNull() && !p.Dead && !p.Spawned && captives.Contains(p) && Find.WorldPawns.Contains(p))
                .InRandomOrder()
                .OrderByDescending(p => p.Faction == Faction.OfPlayer)
                .Take(MaxPerTrader)
                .ToList();
            int next = 0;
            for (int i = 0; i < stock.Count && next < picks.Count; i++)
            {
                if (!(stock[i] is Pawn fresh)) continue;
                fresh.Destroy();
                stock[i] = PutOnSale(xcc, captives, picks[next++]);
            }
            return stock;
        }

        private Pawn PutOnSale(Faction xcc, List<Pawn> captives, Pawn p)
        {
            bool colonist = p.Faction == Faction.OfPlayer;
            Faction takenFrom = p.Faction;
            forSale.Remove(p);
            // Same order as vanilla's recruit: faction first, then off the list (colonists' "lost" thoughts stay).
            p.SetFaction(xcc);
            captives.Remove(p);
            Find.WorldPawns.RemovePawn(p);
            // As Weazel does for its own slave stock, plus everything they carry (sidearms included)
            // and anything valuable they wear.
            p.equipment?.DestroyAllEquipment();
            p.inventory?.DestroyAll();
            StripValuables(p);
            // Buying back a colonist frees them outright; everyone else is bought as a slave.
            bool asSlave = !colonist && ModsConfig.IdeologyActive;
            if (p.guest != null) p.guest.joinStatus = asSlave ? JoinStatus.JoinAsSlave : JoinStatus.JoinAsColonist;
            if (asSlave && takenFrom != null) homeFactions[p] = takenFrom;
            if (colonist)
            {
                onSale.Add(p);
                ownKinds[p] = p.kindDef;
                Messages.Message("WBE_CaptiveOnSale".Translate(p.Named("PAWN"), xcc.Named("FACTION")), MessageTypeDefOf.NeutralEvent);
            }
            // Caravans only offer pawns of the Slave kind (TraderCaravanUtility.GetTraderCaravanRole).
            p.kindDef = PawnKindDefOf.Slave;
            return p;
        }

        // After vanilla's purchase (Pawn.PreTraded) made the pawn a slave of XCC origin.
        public void Notify_Bought(Pawn p)
        {
            if (!homeFactions.TryGetValue(p, out Faction home)) return;
            homeFactions.Remove(p);
            if (p.IsSlave && p.guest != null && !home.defeated) SlaveFactionRef(p.guest) = home;
        }

        private void RestoreOwnKind(Pawn p)
        {
            if (ownKinds.TryGetValue(p, out PawnKindDef kind)) p.kindDef = kind;
            ownKinds.Remove(p);
        }

        // XCC keeps worn apparel worth as much as a load its plunderers bother to carry off.
        public static bool IsValuable(Apparel a) => a.MarketValue >= JobGiver_WeazelSteal.MinLoadValue;

        private static void StripValuables(Pawn p)
        {
            if (p.apparel == null) return;
            foreach (Apparel a in p.apparel.WornApparel.Where(IsValuable).ToList())
            {
                p.apparel.Remove(a);
                a.Destroy();
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreTraded))]
    public static class Patch_PawnPreTraded
    {
        public static void Postfix(Pawn __instance, TradeAction action)
        {
            if (action == TradeAction.PlayerBuys) GameComponent_XccSlaves.Instance?.Notify_Bought(__instance);
        }
    }

    [HarmonyPatch(typeof(KidnappedPawnsTracker), nameof(KidnappedPawnsTracker.KidnappedPawnsTrackerTick))]
    public static class Patch_KidnappedPawnsTrackerTick
    {
        private static readonly AccessTools.FieldRef<KidnappedPawnsTracker, Faction> FactionRef =
            AccessTools.FieldRefAccess<KidnappedPawnsTracker, Faction>("faction");

        public static bool Prefix(KidnappedPawnsTracker __instance)
        {
            Faction faction = FactionRef(__instance);
            if (!XccUtility.IsXcc(faction)) return true;
            var comp = GameComponent_XccSlaves.Instance;
            if (comp == null) return true;
            if (!WeazelBehaviorExperimentsMod.Settings.captiveSlaves)
            {
                // Turned off: vanilla recruits again, but colonists already out on sale still come back.
                if (Find.TickManager.TicksGame % 15051 == 0)
                    comp.ReturnUnsold(faction, __instance.KidnappedPawnsListForReading);
                return true;
            }
            comp.CaptivesTick(faction, __instance.KidnappedPawnsListForReading);
            return false;
        }
    }

    // Weazel's slave merchant stock (WeazelXCCAid.StockGenerator_WeazelPawns with mercenary off).
    [HarmonyPatch]
    public static class Patch_WeazelSlaveStock
    {
        private static Type stockType;
        private static FieldInfo mercenaryField;

        public static bool Prepare()
        {
            stockType = AccessTools.TypeByName("WeazelXCCAid.StockGenerator_WeazelPawns");
            mercenaryField = stockType == null ? null : AccessTools.Field(stockType, "mercenary");
            return mercenaryField != null;
        }

        public static MethodBase TargetMethod() => AccessTools.Method(stockType, "GenerateThings");

        public static void Postfix(object __instance, ref IEnumerable<Thing> __result)
        {
            if (!WeazelBehaviorExperimentsMod.Settings.captiveSlaves || (bool)mercenaryField.GetValue(__instance)) return;
            var comp = GameComponent_XccSlaves.Instance;
            if (comp == null) return;
            __result = comp.FillStock(__result.ToList());
        }
    }
}
