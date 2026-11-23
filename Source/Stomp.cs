using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;
using Verse.Sound;

namespace LIK
{
    /// <summary>
    /// STOMP (user 2026-11-12: "make the knight stomping and the seek and destroy stomp any pawn that gets too close to a knight"; the old
    /// "stomp of sorts" was only Vehicle Framework's run-over collision). Every Knight with its engine running and a pilot aboard - steered or
    /// on Seek &amp; destroy - stamps on hostile pawns that come within a cell of its footprint, about once a second: heavy blunt damage that
    /// grows with the Knight's size, plus a stun, at the target and anything hostile next to it. Pawns caught UNDER the Knight are crushed
    /// (x2.5). Downed enemies are only stomped when they are under it. Never friendlies, never vehicles. Mod setting: on/off.
    /// </summary>
    public class MapComponent_KnightStomp : MapComponent
    {
        const int Interval = 10;
        const int Cooldown = 60;            // ticks between stomps of one Knight
        readonly Dictionary<int, int> next = new Dictionary<int, int>();

        public MapComponent_KnightStomp(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % Interval != 3 || !LIKMod.I.stomp) return;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = pawns.Count - 1; i >= 0; i--)
            {
                VehiclePawn v = pawns[i] as VehiclePawn;
                if (v == null || !v.Spawned || v.Dead || !v.Drafted || !v.HasEnoughOperators) continue;
                if (KnightLoadout.ClassFor(v.VehicleDef) == null || KnightLanding.Recovering(v)) continue;
                int now = Find.TickManager.TicksGame;
                if (next.TryGetValue(v.thingIDNumber, out int when) && now < when) continue;
                try { if (TryStomp(v)) next[v.thingIDNumber] = now + Cooldown; }
                catch (System.Exception e) { Log.ErrorOnce("[LIK] stomp failed for " + v.LabelShort + ": " + e, v.thingIDNumber ^ 0x570); }
            }
        }

        static bool Stompable(Pawn p, VehiclePawn v, bool under)
        {
            if (p == null || p is VehiclePawn || p.Dead || !p.Spawned || !p.HostileTo(v)) return false;
            if (p.Downed && !under) return false;
            return true;
        }

        bool TryStomp(VehiclePawn v)
        {
            CellRect body = v.OccupiedRect();
            CellRect reach = body.ExpandedBy(1).ClipInsideMap(map);
            Pawn target = null; float best = float.MaxValue; bool targetUnder = false;
            foreach (IntVec3 c in reach)
                foreach (Thing t in c.GetThingList(map))
                {
                    Pawn p = t as Pawn;
                    bool under = body.Contains(c);
                    if (!Stompable(p, v, under)) continue;
                    float d = (p.DrawPos - v.DrawPos).sqrMagnitude - (under ? 1000f : 0f);     // anything under the Knight first
                    if (d < best) { best = d; target = p; targetUnder = under; }
                }
            if (target == null) return false;

            int size = Mathf.Max(v.def.size.x, v.def.size.z);
            IntVec3 foot = target.Position;
            var hit = new List<Pawn>();
            foreach (IntVec3 c in GenRadial.RadialCellsAround(foot, 1.5f, true))
            {
                if (!c.InBounds(map)) continue;
                foreach (Thing t in c.GetThingList(map))
                {
                    Pawn p = t as Pawn;
                    if (Stompable(p, v, body.Contains(c)) && !hit.Contains(p)) hit.Add(p);
                }
            }
            foreach (Pawn p in hit)
            {
                bool under = body.Contains(p.Position);
                float dmg = (30f + 8f * size) * (under ? 2.5f : 1f) * (p == target ? 1f : 0.6f);
                if (p.BodySize > 4f) dmg *= 0.5f;           // a thrumbo-sized beast is hurt, not flattened
                p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, dmg, 1.2f, -1f, v));
                if (!p.Dead && p.stances?.stunner != null) p.stances.stunner.StunFor(90, v, false);
            }

            // the blow: the Knight's own footfall, dust, a shake, rubble
            KnightSoundDefs(v)?.PlayOneShot(new TargetInfo(foot, map));
            if (foot.ShouldSpawnMotesAt(map))
                for (int i = 0; i < 3; i++) FleckMaker.ThrowDustPuff(foot.ToVector3Shifted() + Gen.RandomHorizontalVector(1f), map, 1.2f + size * 0.1f);
            ShotFx.Shake(foot.ToVector3Shifted(), map, 1f + size * 0.15f);
            FilthMaker.TryMakeFilth(foot, map, ThingDefOf.Filth_RubbleRock);
            return true;
        }

        static SoundDef KnightSoundDefs(VehiclePawn v)
        {
            KnightClassDef cls = KnightLoadout.ClassFor(v.VehicleDef);
            string key = cls != null ? cls.defName.Replace("LIK_Class_", "") : null;
            return key == null ? null : DefDatabase<SoundDef>.GetNamedSilentFail("LIK_Snd_step_" + key);
        }
    }
}
