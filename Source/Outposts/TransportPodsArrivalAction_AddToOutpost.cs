using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Outposts
{
    public class TransportPodsArrivalAction_AddToOutpost : TransportPodsArrivalAction
    {
        private Outpost outpost;

        public TransportPodsArrivalAction_AddToOutpost()
        {
        }

        public TransportPodsArrivalAction_AddToOutpost(Outpost addTo) => outpost = addTo;

        public override void Arrived(List<ActiveDropPodInfo> pods, int tile)
        {
            var things = new List<Thing>();
            foreach (var thing in pods.SelectMany(pod => pod.innerContainer).OfType<Thing>())
            {
                things.Add(thing);
                if (thing is Pawn)
                {
                	Messages.Message("Outposts.AddedFromTransportPods".Translate(thing.LabelShortCap, outpost.LabelCap),
                        outpost,
                        MessageTypeDefOf.TaskCompletion);
            	}
            }

            foreach (var thing in things)
            {
            	if (thing is Pawn)
                {
                    outpost.AddPawn(thing as Pawn);
                }
                else
                {
                    outpost.AddItem(thing);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref outpost, "outpost");
        }

        public override FloatMenuAcceptanceReport StillValid(IEnumerable<IThingHolder> pods, int destinationTile)
            => outpost.Tile == destinationTile;

        public static IEnumerable<FloatMenuOption> GetFloatMenuOptions(CompLaunchable representative, IEnumerable<IThingHolder> pods, Outpost outpost)
        {
            return TransportPodsArrivalActionUtility.GetFloatMenuOptions(
                () => true,
                () => new TransportPodsArrivalAction_AddToOutpost(outpost),
                "Outposts.AddTo".Translate(outpost.LabelCap),
                representative,
                outpost.Tile,
                launch => launch());
        }
    }
}
