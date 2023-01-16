using Verse;
using Verse.AI.Group;

namespace Outposts
{
    public class LordJob_Deliver : LordJob
    {
        private IntVec3 deliverLoc;

        public LordJob_Deliver()
        {
        }

        public LordJob_Deliver(IntVec3 deliverLoc) => this.deliverLoc = deliverLoc;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref deliverLoc, "deliverLoc");
        }

        public override StateGraph CreateGraph()
        {
            var graph = new StateGraph();
            var travel = new LordToil_Travel(deliverLoc) {maxDanger = Danger.Deadly, useAvoidGrid = true};
            graph.StartingToil = travel;
            var leave = new LordToil_ExitMap(canDig: true);
            graph.AddToil(leave);
            var drop = new LordToil_Drop();
            graph.AddToil(drop);
            var travelToDrop = new Transition(travel, drop);
            travelToDrop.AddTrigger(new Trigger_Memo("TravelArrived"));
            travelToDrop.AddTrigger(new Trigger_PawnHarmed());
            graph.AddTransition(travelToDrop);
            var dropToLeave = new Transition(drop, leave);
            dropToLeave.AddTrigger(new Trigger_Memo(LordToil_Drop.DROPPED_MEMO));
            graph.AddTransition(dropToLeave);
            var gotoDropLoc = new LordToil_GotoDropLoc();
            graph.AddToil(gotoDropLoc);
            var newDropLoc = new Transition(drop, gotoDropLoc);
            newDropLoc.AddTrigger(new Trigger_Memo(LordToil_Drop.DROPPED_MEMO));
            graph.AddTransition(newDropLoc);
            var atDropLoc = new Transition(gotoDropLoc, drop);
            atDropLoc.AddTrigger(new Trigger_Memo("TravelArrived"));
            graph.AddTransition(atDropLoc);
            return graph;
        }
    }
}
