using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Outposts
{
    public class Outpost_VisitorTracker : IExposable
    {
        public Outpost parent;

        public Outpost_VisitorTracker(Outpost parent)
        {
            this.parent = parent;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref parent, "parent");
        }

        public bool HasDockedCaravan() => parent.IsTileCaravanResting();

        public bool HasDockedCaravan(out Caravan caravan) => parent.IsTileCaravanResting(out caravan);

        public Caravan GetDockedCaravan() => HasDockedCaravan() ? parent.GetTileCaravan() : null;

        public bool IsPawnOccupant(Pawn pawn) => parent._occupants.Contains(pawn);

        public bool IsPawnVisitor(Pawn pawn) => parent.VisitorTracker.GetDockedCaravan()?.ContainsPawn(pawn) ?? false;

        public bool TryGetDockedCaravan(out Caravan caravan)
        {
            caravan = GetDockedCaravan();
            return HasDockedCaravan();
        }

        public bool TryGetPawnCaravan(this Pawn pawn, out Caravan caravan)
        {

        }

        public void Tick()
        {
        }
    }
}
