using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Verse;

namespace Outposts
{
    public static class Utils
    {
        public static bool SatisfiedBy(this List<AmountBySkill> minSkills, IEnumerable<Pawn> pawns)
            => minSkills.All(amount => pawns.GetCumulativeSkill(amount.Skill) >= amount.Count);

        public static IEnumerable<Pawn> HumanColonists(this Caravan caravan)
            => caravan.PawnsListForReading.Where(pawn => pawn.IsFreeColonist);

        public static IEnumerable<T> OrEmpty<T>(this IEnumerable<T> source) => source ?? Enumerable.Empty<T>();

        public static IEnumerable<TResult> SelectOrEmpty<TSource, TResult>(
            this IEnumerable<TSource> source,
            Func<TSource, TResult> selector)
        {
            return source is null ? Enumerable.Empty<TResult>() : source.Select(selector);
        }

        public static IEnumerable<TResult> SelectManyOrEmpty<TSource, TResult>(
            this IEnumerable<TSource> source,
            Func<TSource, IEnumerable<TResult>> selector)
        {
            return source is null ? Enumerable.Empty<TResult>() : source.SelectMany(selector);
        }

        public static string Line(this string input, bool show = true)
            => !show || input.NullOrEmpty() ? "" : "\n" + input;

        public static string Line(this TaggedString input, bool show = true)
            => !show || input.NullOrEmpty() ? "" : "\n" + input.RawText;

        public delegate void ImmunityTick(ImmunityHandler immunity);

        public const string VOEDebugCategory = "Vanilla Outposts Expanded";

        public static readonly ImmunityTick ImmunityTickDelegate
            = AccessTools.MethodDelegate<ImmunityTick>(ImmunityTickInfo);

        public static readonly MethodInfo ImmunityTickInfo
            = AccessTools.Method(typeof(ImmunityHandler), "ImmunityHandlerTick");

        // Turn a calculated outpost result into a collection of actual in-game items.
        public static IEnumerable<Thing> MakeResults(this ThingDef thingDef, int count, ThingDef stuff = null)
        {
            // This is already applied from ResultOption.Amount()
            //count = Mathf.RoundToInt(count * OutpostsMod.Settings.ProductionMultiplier);

            var stackCount = count / thingDef.stackLimit;
            var remainder = count % thingDef.stackLimit;

            Thing stack;
            for (int i = 0; i < stackCount; i++)
            {
                stack = ThingMaker.MakeThing(thingDef, stuff);
                stack.stackCount = thingDef.stackLimit;
                yield return stack;
            }

            stack = ThingMaker.MakeThing(thingDef, stuff);
            stack.stackCount = remainder;
            yield return stack;
        }

        public static string Requirement(this string req, bool passed)
            => $"{(passed ? "✓" : "✖")} {req}".Colorize(passed ? Color.green : Color.red);

        public static string Requirement(this TaggedString req, bool passed)
            => $"{(passed ? "✓" : "✖")} {req.RawText}".Colorize(passed ? Color.green : Color.red);

        public static string RequirementsStringBase(this OutpostExtension ext, int tileIndex, IEnumerable<Pawn> pawns)
        {
            var builder = new StringBuilder();
            var biome = Find.WorldGrid[tileIndex].biome;

            var compatiblePawns = ext.GetCompatiblePawns(pawns, out var report);
            if (!compatiblePawns.Any())
            {
                var line = (report.Reason ?? "Outposts.NoValidPawns".Translate()).Requirement(false);
                builder.AppendLine(line);
            }

            if (ext.AllowedBiomes.Any())
            {
                var line = "Outposts.AllowedBiomes"
                    .Translate()
                    .Requirement(ext.AllowedBiomes.Contains(biome));
                builder.AppendLine(line);
                builder.AppendLine(ext.AllowedBiomes.Select(biome => biome.label)
                    .ToLineList("  ", true));
            }

            if (ext.DisallowedBiomes.Any())
            {
                var line = "Outposts.DisallowedBiomes"
                    .Translate()
                    .Requirement(!ext.DisallowedBiomes.Contains(biome));
                builder.AppendLine(line);
                builder.AppendLine(ext.DisallowedBiomes.Select(biome => biome.label)
                    .ToLineList("  ", true));
            }

            if (ext.MinPawns > 0)
            {
                var line = "Outposts.NumPawns"
                    .Translate(ext.MinPawns)
                    .Requirement(compatiblePawns.Count() >= ext.MinPawns);
                builder.AppendLine(line);
            }

            foreach (var requiredSkill in ext.RequiredSkills)
            {
                var line = "Outposts.RequiredSkill"
                    .Translate(requiredSkill.Skill.skillLabel, requiredSkill.Count)
                    .Requirement(compatiblePawns.GetCumulativeSkill(requiredSkill.Skill) >= requiredSkill.Count);
                builder.AppendLine(line);
            }

            if (ext.RequiresGrowing)
            {
                var pass = GenTemperature.TwelfthsInAverageTemperatureRange(
                    tileIndex,
                    Plant.MinOptimalGrowthTemperature,
                    Plant.MaxOptimalGrowthTemperature).Any();
                var line = "Outposts.GrowingRequired"
                    .Translate()
                    .Requirement(pass);
                builder.AppendLine(line);

                // Use the vanilla growing range descriptor for growing zones and map tiles.
                var extra = Zone_Growing.GrowingQuadrumsDescription(tileIndex)
                    .Requirement(pass);
                builder.AppendLine(extra);
            }

            if (ext.CostToMake.Any() && Find.WorldObjects.PlayerControlledCaravanAt(tileIndex) is Caravan caravan)
            {
                foreach (var tdcc in ext.CostToMake)
                {
                    var pass = CaravanInventoryUtility.HasThings(caravan, tdcc.thingDef, tdcc.count);
                    var line = "Outposts.MustHaveInCaravan"
                        .Translate(tdcc.Label)
                        .Requirement(pass);
                    builder.AppendLine(line);
                }
            }

            return builder.ToString();
        }

        public static AcceptanceReport CanAddPawn(this OutpostExtension ext, Pawn pawn)
        {
            if (ext.Event is not null && !IdeoUtility.DoerWillingToDo(ext.Event, pawn))
            {
                return "IdeoligionForbids".Translate();
            }
            return true;
        }

        public static IEnumerable<Pawn> GetCompatiblePawns(this OutpostExtension ext, IEnumerable<Pawn> pawns, out AcceptanceReport report)
        {
            var list = new List<Pawn>();
            report = default;

            foreach (var pawn in pawns)
            {
                var r = ext.CanAddPawn(pawn);
                if (r.Accepted)
                {
                    list.Add(pawn);
                }
                else
                {
                    report = r;
                    break;
                }
            }

            return list;
        }

        // Adding this for whatever weird things I cant think about that leave you stuck.
        [DebugAction(category: VOEDebugCategory, name: "Force End Outpost Raid", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ForceEndMap()
        {
            Map currentMap = Find.CurrentMap;
            if (currentMap.Parent is Outpost parent)
            {
                parent.MapClearAndReset();
                Current.Game.DeinitAndRemoveMap(currentMap);
            }

            if (currentMap.Parent.def.defName == "VOE_AmbushedRaid") // Just cant easily force end this one the same way.
            {
                Messages.Message("Unable to Force End walk colonists out you wont lose them", MessageTypeDefOf.RejectInput, false);
            }
        }

        public static bool IsTileBiomeInvalid(this OutpostExtension ext, int tileIndex)
            => Find.WorldGrid[tileIndex] is var tile && ext.IsTileBiomeInvalid(tile.biome);

        public static bool IsTileBiomeInvalid(this OutpostExtension ext, BiomeDef biome)
            => (ext.DisallowedBiomes.Any() && ext.DisallowedBiomes.Contains(biome))
            || (ext.AllowedBiomes.Any() && !ext.AllowedBiomes.Contains(biome));

        public static bool IsTileBiomeInvalid(this OutpostExtension ext, int tileIndex, out BiomeDef biome)
        {
            var tile = Find.WorldGrid[tileIndex];
            biome = tile?.biome;
            return tile is not null && ext.IsTileBiomeInvalid(tile.biome);
        }

        public static bool IsTileProximityInvalid(int tileIndex)
            => Find.WorldObjects.AnySettlementBaseAtOrAdjacent(tileIndex)
            || IsTileHasOutpostNeighbour(tileIndex);

        public static bool IsTileHasOutpostNeighbour(int tileIndex)
            => GetAllWorldOutposts().Any(outpost => Find.WorldGrid.IsNeighborOrSame(tileIndex, outpost.Tile));

        public static bool IsTileProximityInvalid(this Caravan caravan) => IsTileProximityInvalid(caravan.Tile);

        public static IEnumerable<Outpost> GetAllWorldOutposts() => Find.WorldObjects.AllWorldObjects.OfType<Outpost>();

        public static AcceptanceReport CanSpawnOnTile(this OutpostExtension ext, int tileIndex, IEnumerable<Pawn> pawns)
        {
            var compatiblePawns = ext.GetCompatiblePawns(pawns, out var report);
            if (!compatiblePawns.Any())
            {
                return report.Reason ?? "Outposts.NoValidPawns".Translate();
            }

            if (ext.IsTileBiomeInvalid(tileIndex, out var biome))
            {
                return "Outposts.CannotBeMade".Translate(biome.label);
            }

            if (IsTileProximityInvalid(tileIndex))
            {
                return "Outposts.TooClose".Translate();
            }

            if (ext.MinPawns > 0 && compatiblePawns.Count() < ext.MinPawns)
            {
                return "Outposts.NotEnoughPawns".Translate(ext.MinPawns);
            }

            if (ext.CheckCumulativeSkillThreshold(pawns, out var skillLabel, out var minLevel))
            {
                return "Outposts.NotSkilledEnough".Translate(skillLabel, minLevel);
            }

            // TODO: make PlayerControlledCaravanAt a precondiiton since only caravans can make outposts.
            if (ext.CostToMake.Any() && Find.WorldObjects.PlayerControlledCaravanAt(tileIndex) is Caravan caravan)
            {
                bool predicate(ThingDefCountClass tdcc)
                {
                    return !CaravanInventoryUtility.HasThings(caravan, tdcc.thingDef, tdcc.count);
                }

                // Find the first ingredient that the caravan doesn't have enough of.
                if (ext.CostToMake.FirstOrDefault(predicate) is ThingDefCountClass tdcc)
                {
                    return "Outposts.MustHaveInCaravan".Translate(tdcc.Label);
                }
            }

            return true;
        }

        public static bool CheckCumulativeSkillThreshold(this OutpostExtension ext, IEnumerable<Pawn> pawns, out string skillLabel, out int minLevel)
        {
            skillLabel = "";
            minLevel = 0;
            bool predicate(AmountBySkill required)
            {
                return pawns.CheckCumulativeSkillThreshold(required.Skill, required.Count);
            }

            if (ext.RequiredSkills.Any() && ext.RequiredSkills.FirstOrDefault(predicate) is AmountBySkill amount)
            {
                skillLabel = amount.Skill.skillLabel;
                minLevel = amount.Count;
                return true;
            }

            return false;
        }

        // Determine if the given collection of pawns will collaboratively beat the given threshold.
        public static AcceptanceReport CheckCumulativeSkillThreshold(this IEnumerable<Pawn> pawns, SkillDef skill, int threshold)
        {
            if (GetCumulativeSkill(pawns, skill) < threshold)
            {
                return "Outposts.NotSkilledEnough".Translate(skill.skillLabel, threshold);
            }
            else
            {
                return true;
            }
        }

        // Calculate the cumulative skill level of a group of pawns.
        public static int GetCumulativeSkill(this IEnumerable<Pawn> pawns, SkillDef skill)
            => pawns.Sum(pawn => pawn.skills.GetSkill(skill).Level);

        // Good default for delivery maps and the like.
        public static Map GetNearestPlayerHome(this MapParent self) => self.GetAllPlayerHomes().FirstOrDefault();

        public static IEnumerable<Map> GetAllPlayerHomes(this MapParent self)
            => Find.Maps
            .Where(map => map.IsPlayerHome)
            .OrderBy(map => Find.WorldGrid.ApproxDistanceInTiles(map.Parent.Tile, self.Tile));

        public static Caravan GetTileCaravan(this MapParent map) => Find.WorldObjects.PlayerControlledCaravanAt(map.Tile);

        public static bool IsTileCaravanResting(this MapParent map)
            => map.GetTileCaravan() is Caravan caravan && !caravan.pather.MovingNow;

        public static bool IsTileCaravanResting(this MapParent map, out Caravan caravan)
        {
            caravan = map.GetTileCaravan();
            return caravan is not null && !caravan.pather.MovingNow;
        }

        public static Outpost GetOutpost(this Pawn pawn) => pawn.ParentHolder as Outpost;

        public static bool IsOutpostMember(this Pawn pawn) => pawn.GetOutpost() != null;

        public static bool IsRestingAtOutpost(this Pawn pawn)
        {
            // find outpost at tile from caravan, check if we're resting
        }

        public static void StripOutpostOccupant(Pawn pawn)
        {
            var outpost = pawn.GetOutpost();
            if (outpost != null)
            {
                StripOccupant(outpost, pawn);
            }
        }

        public static void StripOccupant(this Outpost outpost, Pawn pawn)
        {
            if (!outpost.VisitorTracker.IsPawnOccupant(pawn))
            {
                return;
            }

            // Move inventory
            pawn.inventory.innerContainer.TryTransferAllToContainer(outpost._inventory);

            // Move apparel
            if (pawn.Destroyed)
            {
                outpost._inventory.TryAddRangeOrTransfer(pawn.apparel.WornApparel);
            }
            else
            {
                var nonLocked = pawn.apparel.WornApparel.Where(a => !pawn.apparel.IsLocked(a));
                pawn.apparel.WornApparel.RemoveAll(a => nonLocked.Contains(a));
                outpost._inventory.TryAddRangeOrTransfer(nonLocked);
            }

            // Move equipment
            foreach (var item in pawn.equipment.AllEquipmentListForReading)
            {
                pawn.equipment.Remove(item);
                outpost._inventory.TryAdd(item);
            }

            if (pawn.Faction != null)
            {
                pawn.Faction.Notify_MemberStripped(pawn, Faction.OfPlayer);
            }
        }
    }
}