using System.Linq;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Outposts
{
    public class LordToil_Drop : LordToil
    {
        public const string DROPPED_MEMO = "AllDropped";
        public const string AREAFULL_MEMO = "AreaFull";

        public LordToil_Drop() => data = new LordToilData_Drop {TicksPassed = 0};

        public LordToilData_Drop Data => data as LordToilData_Drop;

        public override void UpdateAllDuties()
        {
            foreach (var pawn in lord.ownedPawns) pawn.mindState.duty = new PawnDuty(Outposts_DefOf.VEF_DropAllInInventory);
            Data.TicksPassed = 0;
        }

        public override void LordToilTick()
        {
            base.LordToilTick();
            if (lord.ownedPawns.All(pawn => !pawn.inventory.innerContainer.Any())) lord.ReceiveMemo(DROPPED_MEMO);
            Data.TicksPassed++;
            if (Data.TicksPassed > 60) lord.ReceiveMemo(AREAFULL_MEMO);
        }

        public class LordToilData_Drop : LordToilData
        {
            public int TicksPassed;

            public override void ExposeData()
            {
                Scribe_Values.Look(ref TicksPassed, "ticksPassed");
            }
        }
    }
}