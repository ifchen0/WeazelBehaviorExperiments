using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace WeazelBehaviorExperiments
{
    public class MapComponent_WeazelExperiments : MapComponent
    {
        private const int CheckInterval = 120;
        private static readonly IntRange StrikeTeamDelay = new IntRange(600, 2000);

        // Caravan lords already rolled for, so each caravan gets one chance.
        private HashSet<int> rolledCaravans = new HashSet<int>();
        // Caravan lord id -> tick the strike team arrives.
        private Dictionary<int, int> pendingStrikes = new Dictionary<int, int>();
        // XCC squads called in with a permit, and those of them that took damage.
        private HashSet<int> aidLords = new HashSet<int>();
        private HashSet<int> damagedAidLords = new HashSet<int>();

        public MapComponent_WeazelExperiments(Map map) : base(map)
        {
        }

        public static MapComponent_WeazelExperiments For(Map map) => map?.GetComponent<MapComponent_WeazelExperiments>();

        public void RegisterAidLord(Lord lord) => aidLords.Add(lord.loadID);

        public void Notify_PawnDamaged(Lord lord)
        {
            if (aidLords.Contains(lord.loadID)) damagedAidLords.Add(lord.loadID);
        }

        public bool IsTrackedAidLord(Lord lord) => aidLords.Contains(lord.loadID);

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % CheckInterval != 0) return;

            List<Lord> lords = map.lordManager.lords.ToList();
            CheckCaravans(lords, now);
            foreach (Lord lord in lords)
            {
                if (lord.LordJob is LordJob_WeazelCaravanRaid raid && raid.ShouldPlunder())
                    PlunderUtility.StartPlunder(lord, "WBE_CaravanRaidPlunder");
            }
            CheckAidLords(lords);
        }

        private void CheckCaravans(List<Lord> lords, int now)
        {
            float chance = WeazelBehaviorExperimentsMod.Settings.caravanRaidChance;
            Faction xcc = XccUtility.Xcc;
            foreach (Lord lord in lords)
            {
                if (!(lord.LordJob is LordJob_TradeWithColony) || !rolledCaravans.Add(lord.loadID)) continue;
                if (chance > 0f && CaravanRaidUtility.Eligible(lord, xcc) && Rand.Chance(chance))
                    pendingStrikes[lord.loadID] = now + StrikeTeamDelay.RandomInRange;
            }

            foreach (int id in pendingStrikes.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
            {
                pendingStrikes.Remove(id);
                Lord caravan = lords.FirstOrDefault(l => l.loadID == id);
                if (caravan != null) CaravanRaidUtility.TrySpawnStrikeTeam(caravan);
            }
            rolledCaravans.RemoveWhere(id => !lords.Any(l => l.loadID == id));
        }

        private void CheckAidLords(List<Lord> lords)
        {
            foreach (int id in aidLords.ToList())
            {
                Lord lord = lords.FirstOrDefault(l => l.loadID == id);
                if (lord == null)
                {
                    Forget(id);
                    continue;
                }
                // Vanilla aid hunts enemies until its time is up, then switches to travelling off the map.
                if (lord.CurLordToil is LordToil_HuntEnemies) continue;
                bool undamaged = !damagedAidLords.Contains(id);
                Forget(id);
                if (undamaged && WeazelBehaviorExperimentsMod.Settings.aidPlunder && XccUtility.IsXcc(lord.faction)
                    && !lord.faction.HostileTo(Faction.OfPlayer))
                    PlunderUtility.StartPlunder(lord, "WBE_AidPlunderMessage");
            }
        }

        private void Forget(int id)
        {
            aidLords.Remove(id);
            damagedAidLords.Remove(id);
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref rolledCaravans, "rolledCaravans", LookMode.Value);
            Scribe_Collections.Look(ref pendingStrikes, "pendingStrikes", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref aidLords, "aidLords", LookMode.Value);
            Scribe_Collections.Look(ref damagedAidLords, "damagedAidLords", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                rolledCaravans ??= new HashSet<int>();
                pendingStrikes ??= new Dictionary<int, int>();
                aidLords ??= new HashSet<int>();
                damagedAidLords ??= new HashSet<int>();
            }
        }
    }
}
