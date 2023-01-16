using System.Linq;
using Verse;
using Verse.AI.Group;

namespace Outposts
{
    public class LordToil_GotoDropLoc : LordToil_Travel
    {
        public LordToil_GotoDropLoc() : base(IntVec3.Zero)
        {
        }

        public override void UpdateAllDuties()
        {
            SetDestination(FindDropSpot(lord.ownedPawns.First()));
            base.UpdateAllDuties();
        }

        private IntVec3 FindDropSpot(Pawn pawn)
        {
            if (CellFinder.TryFindRandomReachableCellNear(pawn.Position, pawn.Map, 12.9f * 2f, TraverseParms.For(pawn),
                x => x.Walkable(pawn.Map) &&
                     GenRadial.RadialCellsAround(x, 12.9f, true).Count(c =>
                         c.Walkable(pawn.Map) && !c.GetThingList(pawn.Map).Any(t => t.def.saveCompressible || t.def.category == ThingCategory.Item)) >=
                     GenRadial.NumCellsInRadius(12.9f) / 2, _ => true, out var dropLoc))
                return dropLoc;
            return CellFinder.RandomCell(pawn.Map);
        }
    }
}