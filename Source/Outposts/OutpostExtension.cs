using System.Collections.Generic;
using System.Linq;
using System.Xml;
using RimWorld;
using UnityEngine;
using Verse;

namespace Outposts
{
    public class OutpostExtension : DefModExtension
    {
        public List<BiomeDef> AllowedBiomes;
        public List<ThingDefCountClass> CostToMake;
        public List<BiomeDef> DisallowedBiomes;
        public List<SkillDef> DisplaySkills;
        public HistoryEventDef Event;

        [PostToSettings("Outposts.Setting.MinimumPawns", PostToSettingsAttribute.DrawMode.IntSlider, min: 1f, max: 10f, dontShowAt: 0)]
        public int MinPawns;

        public ThingDef ProvidedFood;

        [PostToSettings("Outposts.Setting.Range", PostToSettingsAttribute.DrawMode.IntSlider, min: 1f, max: 30f, dontShowAt: -1)]
        public int Range = -1;

        public List<AmountBySkill> RequiredSkills;
        public bool RequiresGrowing;
        public List<ResultOption> ResultOptions;

        [PostToSettings("Outposts.Setting.ProductionTime", PostToSettingsAttribute.DrawMode.Time, dontShowAt: -1)]
        public int TicksPerProduction = 15 * 60000;

        [PostToSettings("Outposts.Setting.PackTime", PostToSettingsAttribute.DrawMode.Time)]
        public int TicksToPack = 7 * 60000;

        public int TicksToSetUp = -1;

        // Trims duplicates by wrapping HashSet. 
        public IEnumerable<SkillDef> RelevantSkills => new HashSet<SkillDef>(_RelevantSkills());

        // Will contain duplicates.
        private IEnumerable<SkillDef> _RelevantSkills()
        {
            foreach (var skill in RequiredSkills.OrEmpty())
            {
                yield return skill.Skill;
            }

            foreach (var option in ResultOptions.OrEmpty())
            {
                foreach (var skill in option.AmountsPerSkills)
                {
                    yield return skill.Skill;
                }

                foreach (var skill in option.MinAmountsPerSkills)
                {
                    yield return skill.Skill;
                }
            }
        }
    }

    public class ResultOption
    {
        public int AmountPerPawn;
        public List<AmountBySkill> AmountsPerSkills;
        public int BaseAmount;
        public List<AmountBySkill> MinAmountsPerSkills;
        public ThingDef Thing;

        public int Amount(List<Pawn> pawns)
        {
            var t0 = AmountsPerSkills?.Sum(x => x.Amount(pawns)) ?? 0;
            var t1 = BaseAmount + AmountPerPawn * pawns.Count + t0;
            return Mathf.RoundToInt(t1 * OutpostsMod.Settings.ProductionMultiplier);
        }

        public IEnumerable<Thing> Make(List<Pawn> pawns) => Thing.MakeResults(Amount(pawns));
        public string Explain(List<Pawn> pawns) => $"{Amount(pawns)}x {Thing.LabelCap}";
    }

    public class AmountBySkill
    {
        public int Count;
        public SkillDef Skill;

        public void LoadDataFromXmlCustom(XmlNode xmlRoot)
        {
            if (xmlRoot.ChildNodes.Count != 1)
            {
                Log.Error("Misconfigured AmountBySkill: " + xmlRoot.OuterXml);
                return;
            }

            DirectXmlCrossRefLoader.RegisterObjectWantsCrossRef(this, "Skill", xmlRoot.Name);
            Count = ParseHelper.FromString<int>(xmlRoot.FirstChild.Value);
        }

        public int Amount(List<Pawn> pawns) => Count * pawns.GetCumulativeSkill(Skill);
    }
}