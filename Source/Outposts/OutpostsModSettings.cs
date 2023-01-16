using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Outposts
{
    public class OutpostsModSettings : ModSettings
    {
        public DeliveryMethod DeliveryMethod = DeliveryMethod.Teleport;
        public bool DoRaids = true;
        public float ProductionMultiplier = 1f;
        public Dictionary<string, OutpostSettings> SettingsPerOutpost = new();
        public float TimeMultiplier = 1f;
        public float RaidDifficultyMultiplier = 1f;
        public IntRange raidTimeInterval = new(GenDate.TicksPerQuadrum / 2, GenDate.TicksPerQuadrum);

        public OutpostSettings SettingsFor(Outpost outpost) => SettingsFor(outpost.def.defName);

        public OutpostSettings SettingsFor(string defName)
        {
            if (!SettingsPerOutpost.TryGetValue(defName, out var setting) || setting is null)
            {
                setting = new OutpostSettings()
                {
                    Name = defName
                };
                SettingsPerOutpost.SetOrAdd(defName, setting);
            }

            return setting;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ProductionMultiplier, "productionMultiplier", defaultValue: 1f);
            Scribe_Values.Look(ref TimeMultiplier, "timeMultiplier", defaultValue: 1f);
            Scribe_Values.Look(ref DeliveryMethod, "deliveryMethod");
            Scribe_Collections.Look(ref SettingsPerOutpost, "settingsPerOutpost", keyLookMode: LookMode.Value, valueLookMode: LookMode.Deep);
            Scribe_Values.Look(ref DoRaids, "doRaids", defaultValue: true);
            Scribe_Values.Look(ref RaidDifficultyMultiplier, "RaidDifficultyMultiplier", defaultValue: 1f);
            
            var RaidMinDays = raidTimeInterval.min; // TODO: is this ticks?
            var RaidMaxDays = raidTimeInterval.max;
            Scribe_Values.Look(ref RaidMinDays, "RaidMinDays", defaultValue: 600000);
            Scribe_Values.Look(ref RaidMaxDays, "RaidMaxDays", defaultValue: 1800000);
            raidTimeInterval = new IntRange(RaidMinDays, RaidMaxDays);
        }

        public class OutpostSettings : Dictionary<string, string>, IExposable
        {
            public string Name;

            public void ExposeData()
            {
                Dictionary<string, string> self = this;
                Scribe_Collections.Look(ref self, "keysToValues", keyLookMode: LookMode.Value, valueLookMode: LookMode.Value);
            }

            public bool Has(string key) => this.ContainsKey(key);

            public bool TryGetValue(string key, Type type, out object value)
            {
                var pass = this.TryGetValue(key, out var temp);
                value = pass ? ParseHelper.FromString(temp, type) : null;
                return pass;
            }

            public bool TryGetValue<T>(string key, out T value)
            {
                var pass = this.TryGetValue(key, out var temp);
                value = pass ? ParseHelper.FromString<T>(temp) : default;
                return pass;
            }

            public T GetValue<T>(string key) where T : new() => ParseHelper.FromString<T>(this[key]);

            public void Set<T>(string key, T value) => this.SetOrAdd(key, value.ToString());
        }
    }
}