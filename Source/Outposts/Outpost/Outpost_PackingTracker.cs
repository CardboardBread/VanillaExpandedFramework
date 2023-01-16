using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static System.Net.Mime.MediaTypeNames;

namespace Outposts
{
    public class Outpost_PackingTracker : IExposable, ISelectable
    {
        public Outpost parent;
        internal int _ticksTillPacked = -1;

        public virtual int TicksToPack => (parent.Ext?.TicksToPack ?? 7 * 60000) / parent._occupants.Count;
        public bool Packing => _ticksTillPacked > 0;

        public Outpost_PackingTracker(Outpost parent)
        {
            this.parent = parent;
        }

        public void Tick()
        {
            if (Packing)
            {
                _ticksTillPacked--;
                if (_ticksTillPacked <= 0)
                {
                    parent.ConvertToCaravan();
                }
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref _ticksTillPacked, "ticksTillPacked");
        }

        public IEnumerable<Gizmo> GetGizmos()
        {
            if (Packing)
            {
                // 'Stop Packing' gizmo.
                yield return new Command_Action
                {
                    action = () => _ticksTillPacked = -1,
                    defaultLabel = "Outposts.Commands.StopPack.Label".Translate(),
                    defaultDesc = "Outposts.Commands.StopPack.Desc".Translate(),
                    icon = Tex.StopPackTex
                };
            }
            else
            {
                // 'Start Packing' gizmo.
                yield return new Command_Action
                {
                    action = () => _ticksTillPacked = Mathf.RoundToInt(TicksToPack * OutpostsMod.Settings.TimeMultiplier),
                    defaultLabel = "Outposts.Commands.Pack.Label".Translate(),
                    defaultDesc = "Outposts.Commands.Pack.Desc".Translate(),
                    icon = Tex.PackTex
                };
            }

            if (Prefs.DevMode && Packing)
            {
                // Debug 'Pack Now' gizmo.
                yield return new Command_Action
                {
                    action = () => _ticksTillPacked = 1,
                    defaultLabel = "Dev: Pack now",
                    defaultDesc = "Reduce ticksTillPacked to 1"
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
    }
}
