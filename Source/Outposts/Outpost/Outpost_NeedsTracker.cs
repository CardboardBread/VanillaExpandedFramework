using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Outposts
{
    public class Outpost_NeedsTracker
    {
        [PostToSettings("Outposts.Setting.RestEffectiveness", PostToSettingsAttribute.DrawMode.IntSlider, min: 0.1f, max: 2.0f, dontShowAt: -1f)]
        public static float OutpostRestEffectiveness = 0.75f;

        public Outpost parent;

        public Outpost_NeedsTracker(Outpost parent)
        {
            this.parent = parent;
        }

        public virtual float RestPerTickResting => 0.005714286f * 2.5f;

        // TODO: take pawn as argument, take night owl into consideration.
        public bool IsPawnRestTime()
        {
            return GenLocalDate.HourInteger(parent.Tile) >= 23 || GenLocalDate.HourInteger(parent.Tile) <= 5;
        }

        public void Tick()
        {
            SatisfyVisitorNeeds();

            // Probably shouldnt be doing this during a raid. Fixed one bug in there, but really it just shouldnt be happening.
            if (!parent.HasMap)
            {
                SatisfyOccupantNeeds();
            }
        }

        public bool IsPawnSatisfyable(Pawn pawn)
        {
            if (pawn is null || pawn.Spawned || pawn.Dead)
            {
                return false;
            }

            return true;
        }

        public void SatisfyOccupantNeeds()
        {
            foreach (var pawn in parent.AllPawns)
            {
                SatisfyOccupantNeeds(pawn);
            }
        }

        public void SatisfyOccupantNeeds(Pawn pawn)
        {
            if (!IsPawnSatisfyable(pawn) || !parent.VisitorTracker.IsPawnOccupant(pawn))
            {
                return;
            }

            pawn.ageTracker?.AgeTick();

            if (IsPawnRestTime())
            {
                pawn.needs?.rest?.TickResting(OutpostRestEffectiveness);
            }

            OcccupantHealthTick(pawn);
            // TODO: Replace with harmony patch.
            if (pawn.Dead)
            {
                parent.Notify_MemberDied(pawn);
            }

            SatisfyFoodNeed(pawn);
        }

        public void SatisfyVisitorNeeds()
        {
            if (parent.VisitorTracker.TryGetDockedCaravan(out var caravan))
            {
                foreach (var pawn in caravan.PawnsListForReading)
                {
                    SatisfyVisitorNeeds(pawn);
                }
            }
        }

        public void SatisfyVisitorNeeds(Pawn pawn)
        {
            if (!IsPawnSatisfyable(pawn) || !parent.VisitorTracker.IsPawnVisitor(pawn))
            {
                return;
            }

            if (pawn.needs?.rest is Need_Rest need)
            {
                // caravans already fulfill pawn rest need when resting
                //need.CurLevel += RestPerTickResting;
            }

            SatisfyFoodNeed(pawn);
        }

        public virtual void OcccupantHealthTick(Pawn pawn)
        {
            // Just in case the birthday killed them XD
            if (pawn.health?.hediffSet == null || pawn.Dead)
            {
                return;
            }

            // Hediff ticks and immunities.
            var removedAnything = false;
            foreach (var hediff in pawn.health.hediffSet.hediffs)
            {
                try
                {
                    hediff.Tick();
                    hediff.PostTick();
                }
                catch
                {
                    pawn.health.RemoveHediff(hediff);
                }

                if (pawn.Dead)
                {
                    return;
                }

                if (hediff.ShouldRemove)
                {
                    pawn.health.hediffSet.hediffs.Remove(hediff);
                    hediff.PostRemoved();
                    removedAnything = true;
                }
            }

            if (removedAnything)
            {
                pawn.health.Notify_HediffChanged(null);
                removedAnything = false;
            }

            Utils.ImmunityTickDelegate(pawn.health.immunity);

            // Tending, injuries and healing every 600 ticks.
            if (pawn.IsHashIntervalTick(600))
            {
                if (pawn.health.HasHediffsNeedingTend())
                {
                    var doctor = parent.AllPawns.Where(p => p.RaceProps.Humanlike && !p.Downed).MaxBy(p => p.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? -1f);
                    if (doctor != null)
                    {
                        Medicine medicine = null;
                        var potency = 0f;
                        parent.CheckNoDestroyedOrNoStack();
                        foreach (var thing in _inventory.ToList())
                            if (thing.def.IsMedicine && (pawn.playerSettings is null || pawn.playerSettings.medCare.AllowsMedicine(thing.def)))
                            {
                                var statValue = thing.GetStatValue(StatDefOf.MedicalPotency);
                                if (statValue > potency || medicine == null)
                                {
                                    potency = statValue;
                                    medicine = (Medicine)TakeItem(thing);
                                }
                            }

                        TendUtility.DoTend(doctor, pawn, medicine);
                    }
                }

                if (pawn.health.hediffSet.HasNaturallyHealingInjury())
                {
                    var injuries = new List<Hediff_Injury>();
                    pawn.health.hediffSet.GetHediffs(ref injuries, x => x.CanHealNaturally());
                    var injury = injuries.RandomElement();
                    injury.Heal(pawn.HealthScale * pawn.GetStatValue(StatDefOf.InjuryHealingFactor));
                    if (injury.ShouldRemove)
                    {
                        pawn.health.hediffSet.hediffs.Remove(injury);
                        injury.PostRemoved();
                        removedAnything = true;
                    }
                }

                if (pawn.health.hediffSet.HasTendedAndHealingInjury())
                {
                    var injuries = new List<Hediff_Injury>();
                    pawn.health.hediffSet.GetHediffs(ref injuries, x => x.CanHealFromTending());
                    var injury = injuries.RandomElement();
                    injury.Heal(GenMath.LerpDouble(0f, 1f, 0.5f, 1.5f, Mathf.Clamp01(injury.TryGetComp<HediffComp_TendDuration>().tendQuality)) * pawn.HealthScale *
                                pawn.GetStatValue(StatDefOf.InjuryHealingFactor));
                    if (injury.ShouldRemove)
                    {
                        pawn.health.hediffSet.hediffs.Remove(injury);
                        injury.PostRemoved();
                        removedAnything = true;
                    }
                }

                if (removedAnything) pawn.health.Notify_HediffChanged(null);
            }
        }

        public void SatisfyFoodNeed(Pawn pawn)
        {
            // Eat once every 300 ticks.
            if (pawn.IsHashIntervalTick(300))
            {
                if (pawn.needs?.food is Need_Food need &&
                    need.CurLevelPercentage <= pawn.RaceProps.FoodLevelPercentageWantEat &&
                    parent.ProvidedFood.IsNutritionGivingIngestible is true &&
                    parent.ProvidedFood.ingestible.HumanEdible)
                {
                    var thing = ThingMaker.MakeThing(parent.ProvidedFood);
                    if (thing.IngestibleNow && pawn.RaceProps.CanEverEat(thing))
                    {
                        need.CurLevel += thing.Ingested(pawn, need.NutritionWanted);
                    }
                }
            }
        }
    }
}
