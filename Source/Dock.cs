using System.Collections.Generic;
using System.Linq;
using RimWorld;
using SmashTools;
using Vehicles;
using Verse;
using Verse.AI;

namespace LIK
{
    /// <summary>Right-click a hangar with a running Knight selected: the Knight drives in and parks centred in the bay, ready to be serviced.</summary>
    public class FloatMenuOptionProvider_DockKnight : FloatMenuOptionProvider_Vehicle
    {
        protected override bool Multiselect => false;

        protected override bool SelectedVehicleValid(VehiclePawn vehicle, FloatMenuContext context)
        {
            return vehicle.Faction == Faction.OfPlayer && KnightLoadout.ClassFor(vehicle.VehicleDef) != null;
        }

        protected override FloatMenuOption GetSingleOptionFor(Thing clickedThing, FloatMenuContext context)
        {
            var hangar = clickedThing as Building_KnightHangar;
            var knight = context.FirstSelectedPawn as VehiclePawn;
            if (hangar == null || knight == null || !hangar.Spawned) return null;
            KnightClassDef cls = KnightLoadout.ClassFor(knight.VehicleDef);
            if (cls == null) return null;

            string label = "Dock " + knight.LabelShort + " in the hangar";
            if (!hangar.Supports(cls))
                return new FloatMenuOption(label + " (this bay is too " + (cls.vehicle.size.x > 6 ? "small" : "large") + " for a " + cls.label + ")", null);
            VehiclePawn already = hangar.ServicedKnight;
            if (already != null && already != knight)
                return new FloatMenuOption(label + " (" + already.LabelShort + " is already docked)", null);
            IntVec3 dest = hangar.KnightCell(knight.VehicleDef);          // feet on the drawn bay (was the hangar centre: Knight stood left of and below the bay)
            if (!knight.CanMoveFinal || !knight.CanReachVehicle(dest, PathEndMode.OnCell, Danger.Deadly, TraverseMode.ByPawn))
                return new FloatMenuOption(label + " (cannot reach it)", null);

            return new FloatMenuOption(label, delegate
            {
                Job job = JobMaker.MakeJob(JobDefOf.Goto, dest);
                job.locomotionUrgency = LocomotionUrgency.Walk;
                if (knight.jobs.TryTakeOrderedJob(job, JobTag.Misc, false))
                {
                    knight.vehiclePather.SetEndRotation(hangar.Geo.Rot8);   // facing the apron (south / west / east hangar)
                    hangar.OrderDock(knight);                     // clamped into the cradle on arrival (docking = stowing, 2026-11-22)
                }
            });
        }
    }
}
