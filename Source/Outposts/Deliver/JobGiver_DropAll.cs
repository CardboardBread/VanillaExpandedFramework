using Verse;
using Verse.AI;

namespace Outposts
{
    public class JobGiver_DropAll : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn?.inventory is null) return null;
            pawn.inventory.UnloadEverything = true;
            pawn.inventory.DropAllNearPawn(pawn.Position, false, true);
            return null;
        }
    }
}