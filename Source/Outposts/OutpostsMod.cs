using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Outposts
{
    public class OutpostsMod : Mod
    {
        public static Harmony Harm = new("vanillaexpanded.outposts");
        public static OutpostsModSettings Settings;
        public static List<WorldObjectDef> OutpostDefs;

        private static Dictionary<Type, List<FieldInfo>> editableFields;
        private Dictionary<WorldObjectDef, float> sectionHeights;
        private float prevHeight = float.MaxValue;
        private Vector2 scrollPos;

        public OutpostsMod(ModContentPack content) : base(content)
        {
            LongEventHandler.ExecuteWhenFinished(FindOutposts);
            Settings = base.GetSettings<OutpostsModSettings>();
            editableFields = new Dictionary<Type, List<FieldInfo>>();

            foreach (var type in GetAllOutpostTypes())
            {
                var targets = new List<FieldInfo>();
                editableFields[type] = targets;
                foreach (var field in type.GetFields(AccessTools.all))
                {
                    if (field.HasAttribute<PostToSettingsAttribute>())
                    {
                        targets.Add(field);
                    }
                }
            }
        }

        public static IEnumerable<Type> GetAllOutpostTypes()
        {
            yield return typeof(Outpost);
            yield return typeof(OutpostExtension);

            foreach (var type in typeof(Outpost).AllSubclasses())
            {
                yield return type;
            }
            
            foreach (var type in typeof(OutpostExtension).AllSubclasses())
            {
                yield return type;
            }
        }

        public static IEnumerable<WorldObjectDef> GetAllOutpostDefs()
            => DefDatabase<WorldObjectDef>.AllDefs
            .Where(def => typeof(Outpost).IsAssignableFrom(def.worldObjectClass));

        private void FindOutposts()
        {
            OutpostDefs = GetAllOutpostDefs().ToList();
            sectionHeights = OutpostDefs.ToDictionary(outpostDef => outpostDef, _ => float.MaxValue);
            
            if (OutpostDefs.Any())
            {
                HarmonyPatches.DoConditionalPatches();
                Outposts_DefOf.VEF_OutpostDeliverySpot.designationCategory = DefDatabase<DesignationCategoryDef>.GetNamed("Misc");               
            }
        }

        public static void Notify_Spawned(Outpost outpost)
        {
            InitializeSettings(outpost);
        }

        private static void InitializeSettings(Outpost outpost)
        {
            var settings = Settings.SettingsFor(outpost.def.defName);
            foreach (var field in editableFields[outpost.GetType()])
            {
                if (field.TryGetAttribute<PostToSettingsAttribute>(out var attr))
                {
                    var qualifiedName = $"{field.DeclaringType.Name}.{field.Name}";
                    var get = settings.TryGetValue(qualifiedName, field.FieldType, out var value);
                    field.SetValue(outpost, get ? value : attr.Default ?? field.GetValue(outpost));
                }
            }

            foreach (var field in editableFields[outpost.Ext.GetType()])
            {
                if (field.TryGetAttribute<PostToSettingsAttribute>(out var attr))
                {
                    var label = $"{field.DeclaringType.Name}.{field.Name}";
                    var get = settings.TryGetValue(label, field.FieldType, out var value);
                    field.SetValue(outpost.Ext, get ? value : attr.Default ?? field.GetValue(outpost.Ext));
                }
            }
        }

        public static void Notify_Removed(Outpost outpost)
        {
        }

        public override string SettingsCategory() => OutpostDefs.Any() ? "Outposts.Settings.Title".Translate() : null;

        public override void DoSettingsWindowContents(Rect inRect)
        {
            base.DoSettingsWindowContents(inRect);
            var viewRect = new Rect(0, 0, inRect.width - 20, prevHeight);
            Widgets.BeginScrollView(inRect, ref scrollPos, viewRect);
            var listing = new Listing_Standard();
            listing.Begin(viewRect);
            listing.Label("Outposts.Settings.Multiplier.Production".Translate(Settings.ProductionMultiplier.ToStringPercent()));
            Settings.ProductionMultiplier = listing.Slider(Settings.ProductionMultiplier, 0.1f, 10f);
            listing.Label("Outposts.Settings.Multiplier.Time".Translate(Settings.TimeMultiplier.ToStringPercent()));
            Settings.TimeMultiplier = listing.Slider(Settings.TimeMultiplier, 0.01f, 5f);
            if (listing.ButtonTextLabeled("Outposts.Settings.DeliveryMethod".Translate(), $"Outposts.Settings.DeliveryMethod.{Settings.DeliveryMethod}".Translate()))
            {
                Find.WindowStack.Add(new FloatMenu(Enum.GetValues(typeof(DeliveryMethod)).OfType<DeliveryMethod>().Select(method => new FloatMenuOption(
                    $"Outposts.Settings.DeliveryMethod.{method}".Translate(), () => Settings.DeliveryMethod = method)).ToList()));
            }

            listing.CheckboxLabeled("Outposts.Settings.DoRaids".Translate(), ref Settings.DoRaids);
            listing.Label("Outposts.Settings.RaidFrequency".Translate());
            listing.Label($"{Settings.raidTimeInterval.min.ToStringTicksToPeriodVerbose(false)} - {Settings.raidTimeInterval.max.ToStringTicksToPeriodVerbose(false)}");
            listing.IntRange(ref Settings.raidTimeInterval, GenDate.TicksPerDay, GenDate.TicksPerYear*2);
            listing.Label("Outposts.Settings.RaidDifficulty".Translate(Settings.RaidDifficultyMultiplier.ToStringPercent()));
            Settings.RaidDifficultyMultiplier = listing.Slider(Settings.RaidDifficultyMultiplier, 0.01f, 10f);

            listing.GapLine();

            static void DoSetting(Listing_Standard listing, OutpostsModSettings.OutpostSettings settings, FieldInfo info, object obj = null)
            {
                if (info.TryGetAttribute<PostToSettingsAttribute>(out var attr))
                {
                    var key = $"{info.DeclaringType.Name}.{info.Name}";
                    var current = settings.TryGetValue(key, info.FieldType, out var value) ? value : obj is null ? attr.Default : info.GetValue(obj);
                    attr.Draw(listing, ref current);
                    if (current == attr.Default)
                    {
                        if (settings.Has(key)) settings.Remove(key);
                    }
                    else
                    {
                        settings.Set(key, current);
                    }
                }
            }

            foreach (var outpost in OutpostDefs)
            {
                var section = listing.BeginSection(sectionHeights[outpost]);
                section.Label(outpost.LabelCap);
                var settings = Settings.SettingsFor(outpost.defName);
                foreach (var info in editableFields[outpost.worldObjectClass]) DoSetting(section, settings, info);
                if (outpost.GetModExtension<OutpostExtension>() is { } ext)
                {
                    foreach (var info in editableFields[ext.GetType()])
                    {
                        DoSetting(section, settings, info, ext);
                    }
                }

                sectionHeights[outpost] = section.CurHeight;
                listing.EndSection(section);
                listing.Gap();
            }
            
            prevHeight = listing.CurHeight;
            listing.End();
            Widgets.EndScrollView();
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
            if (Find.World?.worldObjects is WorldObjectsHolder holder)
            {
                foreach (var outpost in holder.AllWorldObjects.OfType<Outpost>())
                {
                    InitializeSettings(outpost);
                }
            }
        }
    }
}
