using RimWorld;
using Verse;

namespace Outposts
{
    public class Outpost_RaidTracker : IExposable
    {
        public float raidPoints;
        public Faction raidFaction;

        public Outpost parent;

        public Outpost_RaidTracker(Outpost parent)
        {
            this.parent = parent;
        }

        public void Reset()
        {
            raidFaction = null;
            raidPoints = 0f;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref raidFaction, "raidFaction");
            Scribe_Values.Look(ref raidPoints, "raidPoints");
        }
    }
}
