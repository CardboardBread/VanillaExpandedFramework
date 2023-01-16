using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Outposts
{
    public class Outpost_ProductionTracker : IExposable, ISelectable
    {
        public Outpost parent;
        internal int ticksTillProduction;

        public virtual int TicksPerProduction => parent.Ext?.TicksPerProduction ?? 15 * 60000;

        public virtual string TimeTillProduction 
            => ticksTillProduction
            .ToStringTicksToPeriodVerbose()
            .Colorize(ColoredText.DateTimeColor);

        public void ExposeData()
        {
            Scribe_Values.Look(ref ticksTillProduction, "ticksTillProduction");
        }

        public IEnumerable<Gizmo> GetGizmos()
        {
            if (Prefs.DevMode)
            {
                // 'Produce Now' dev gizmo.
                yield return new Command_Action
                {
                    action = () => ticksTillProduction = 10,
                    defaultLabel = "Dev: Produce now",
                    defaultDesc = "Reduce ticksTillProduction to 10"
                };
            }
        }

        public string GetInspectString()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<InspectTabBase> GetInspectTabs()
        {
            throw new NotImplementedException();
        }

        public void PostAdd()
        {
            ticksTillProduction = Mathf.RoundToInt(TicksPerProduction * OutpostsMod.Settings.TimeMultiplier);
        }

        public void Tick()
        {
            if (!parent.PackingTracker?.Packing ?? false && TicksPerProduction > 0)
            {
                ticksTillProduction--;
                if (ticksTillProduction <= 0)
                {
                    ticksTillProduction = Mathf.RoundToInt(TicksPerProduction * OutpostsMod.Settings.TimeMultiplier);
                    parent.Produce();
                }
            }
        }
    }
}
