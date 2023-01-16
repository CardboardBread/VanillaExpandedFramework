using Outposts;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI.Group;
using Verse.AI;
using KCSG;

namespace Outposts
{
    public class Outpost : MapParent, IThingHolder, IExposable, ILoadReferenceable, ISelectable
    {
        public const int InvalidRange = -1;
        
        public string Name;
        internal Material _material;
        public override Color ExpandingIconColor => Faction.Color;

        internal OutpostExtension _extension;
        public Map DeliveryMap;

        internal ThingOwner<Thing> _inventory;
        internal ThingOwner<Pawn> _occupants;

        internal bool _costPaid;
        internal bool _skillsDirty = true;

        public Outpost_PackingTracker PackingTracker;
        public Outpost_ProductionTracker ProductionTracker;
        public Outpost_NeedsTracker NeedsTracker;
        public Outpost_VisitorTracker VisitorTracker;
        public Outpost_RaidTracker RaidTracker;

        public override bool HasName => !Name.NullOrEmpty();
        public override string Label => Name;
        public virtual int Range => Ext?.Range ?? InvalidRange;
        public virtual bool IsRangeValid => Range != InvalidRange;
        public IEnumerable<Thing> Things => _inventory;
        public IEnumerable<Pawn> AllPawns => _occupants;
        public int PawnCount => _occupants.Count;
        public IEnumerable<Pawn> CapablePawns => _occupants.InnerListForReading.Where(IsCapable);
        public override Material Material
            => _material ??= MaterialPool.MatFrom(
                Faction.def.settlementTexturePath,
                ShaderDatabase.WorldOverlayTransparentLit,
                Faction.Color,
                WorldMaterials.WorldObjectRenderQueue);
        public virtual ThingDef ProvidedFood => Ext?.ProvidedFood ?? ThingDefOf.MealSimple;
        public OutpostExtension Ext => _extension ??= def.GetModExtension<OutpostExtension>();
        public virtual List<ResultOption> ResultOptions => Ext.ResultOptions;
        public override MapGeneratorDef MapGeneratorDef
        {
            get
            {
                if (def.GetModExtension<CustomGenOption>() is CustomGenOption cGen &&
                    (cGen.chooseFromlayouts.Count > 0 || cGen.chooseFromSettlements.Count > 0))
                {
                    return DefDatabase<MapGeneratorDef>.GetNamed("KCSG_WorldObject");
                }

                return MapGeneratorDefOf.Base_Faction;
            }
        }

        public Outpost()
        {
            // TODO: should keep maxStacks at the default of 999999.
            _inventory = new(this);
            _occupants = new(this);
        }

        // TryAddOrTransfer() allows selecting things regardless of their ownership.
        public void AddItem(Thing thing) => _inventory.TryAddOrTransfer(thing);

        public void AddItem(Thing thing, int count) => _inventory.TryAddOrTransfer(thing, count);

        public void AddItems(IEnumerable<Thing> things) => _inventory.TryAddRangeOrTransfer(things);

        public Thing TakeItem(Thing thing) => _inventory.Take(thing);

        public Thing TakeItem(Thing thing, int count) => _inventory.Take(thing, count);

        public IEnumerable<Thing> TakeItems(IEnumerable<Thing> things)
        {
            foreach (var thing in things)
            {
                yield return TakeItem(thing);
            }
        }

        public override void PostAdd()
        {
            base.PostAdd();
            ProductionTracker.PostAdd();
        }

        public override void PostRemove()
        {
            base.PostRemove();
            OutpostsMod.Notify_Removed(this);
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            if (Range > 0) GenDraw.DrawWorldRadiusRing(Tile, Range);
        }

        public override void GetChildHolders(List<IThingHolder> outChildren)
        {
            base.GetChildHolders(outChildren);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref Name, "name");
            Scribe_Deep.Look(ref _occupants, "occupants", this);
            Scribe_Deep.Look(ref _inventory, "containedItems", this);
            Scribe_Values.Look(ref _costPaid, "costPaid");
            Scribe_References.Look(ref DeliveryMap, "deliveryMap");
            RecachePawnTraits();
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan)
        {
            return base.GetFloatMenuOptions(caravan);
        }

        public override IEnumerable<FloatMenuOption> GetTransportPodsFloatMenuOptions(IEnumerable<IThingHolder> pods, CompLaunchable representative)
        {
            foreach (var option in base.GetTransportPodsFloatMenuOptions(pods, representative))
            {
                yield return option;
            }
            
            foreach (var option in TransportPodsArrivalAction_AddToOutpost.GetFloatMenuOptions(representative, pods, this))
            {
                yield return option;
            }
        }

        public override IEnumerable<FloatMenuOption> GetShuttleFloatMenuOptions(IEnumerable<IThingHolder> pods, Action<int, TransportPodsArrivalAction> launchAction)
        {
            return base.GetShuttleFloatMenuOptions(pods, launchAction);
        }

        public override void Tick()
        {
            base.Tick();
            PackingTracker.Tick();
            ProductionTracker.Tick();
            NeedsTracker.Tick();
        }

        public virtual IEnumerable<Thing> ProducedThings()
        {
            return ResultOptions.SelectMany(option => option.Make(CapablePawns.ToList()));
        }

        public virtual void Produce()
        {
            Deliver(ProducedThings());
        }

        public override void SpawnSetup()
        {
            base.SpawnSetup();            
            if (DeliveryMap == null)
            {
                DeliveryMap = this.GetNearestPlayerHome();
            }
            RecachePawnTraits();
            OutpostsMod.Notify_Spawned(this);
        }

        public virtual void RecachePawnTraits()
        {
            _skillsDirty = true;
            foreach (var pawn in _inventory.OfType<Pawn>().ToList())
            {
                _inventory.Remove(pawn);
                if (pawn.GetCaravan() is Caravan caravan)
                {
                    caravan.RemovePawn(pawn);
                }

                AddPawn(pawn);
            }
        }

        public bool AddPawn(Pawn pawn)
        {
            if (!Ext.CanAddPawn(pawn).Accepted)
            {
                return false;
            }

            if (pawn.GetCaravan() is Caravan caravan)
            {
                // Move pawn's held items to other caravan members before they leave.
                var ownedItems = CaravanInventoryUtility.AllInventoryItems(caravan)
                    .Where(item => CaravanInventoryUtility.GetOwnerOf(caravan, item) == pawn);
                foreach (var item in ownedItems)
                {
                    CaravanInventoryUtility.MoveInventoryToSomeoneElse(pawn, item, caravan.PawnsListForReading, new List<Pawn> { pawn }, item.stackCount);                    
                }

                // Move all caravan items to outpost, if no humanlikes are left in the caravan.
                // Have to empty every pawns inventory items first or they will get added with the things on them. Creating duplicate load IDs/items
                // Move either fails or moves it to an animal. Neither result work
                var IsAnyOtherHumanlike = caravan.PawnsListForReading.Except(pawn).Any(pawn => pawn.RaceProps.Humanlike);
                if (!IsAnyOtherHumanlike)
                {
                    foreach (var item in CaravanInventoryUtility.AllInventoryItems(caravan))
                    {
                        _inventory.TryAddOrTransfer(item);
                    }
                }

                pawn.ownership.UnclaimAll();
                caravan.RemovePawn(pawn);

                var IsHumanlikeRemaining = caravan.PawnsListForReading.Any(pawn => pawn.RaceProps.Humanlike);
                if (!IsHumanlikeRemaining)
                {
                    _inventory.TryAddRangeOrTransfer(caravan.AllThings);
                    if (!_costPaid && Ext.CostToMake?.Count > 0)
                    {
                        var costs = Ext.CostToMake.Select(tdcc => new ThingDefCountClass(tdcc.thingDef, tdcc.count)).ToList();
                        _inventory.RemoveAll(thing =>
                        {
                            if (costs.FirstOrDefault(tdcc => tdcc.thingDef == thing.def) is not { } cost) return false;
                            if (cost.count > thing.stackCount)
                            {
                                cost.count -= thing.stackCount;
                                return true;
                            }

                            if (cost.count < thing.stackCount)
                            {
                                thing.stackCount -= cost.count;
                                costs.Remove(cost);
                                return false;
                            }

                            costs.Remove(cost);
                            return true;
                        });
                        if (!costs.Any()) _costPaid = true;
                    }

                    caravan.Destroy();
                }
            }

            pawn.holdingOwner?.Remove(pawn);

            if (Find.WorldPawns.Contains(pawn))
            {
                Find.WorldPawns.RemovePawn(pawn);
            }

            if (!_occupants.Contains(pawn))
            {
                _occupants.TryAdd(pawn);
            }

            RecachePawnTraits();
            return true;
        }

        public void ConvertToCaravan()
        {
            var caravan = CaravanMaker.MakeCaravan(_occupants, Faction, Tile, true);
            if (_inventory is not null)
                foreach (var item in _inventory.Except(caravan.AllThings))
                    caravan.AddPawnOrItem(item, true);
            if (Find.WorldSelector.IsSelected(this)) Find.WorldSelector.Select(caravan, false);
            Destroy();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            // Inherited gizmos.
            foreach (var gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            // Packing gizmos; start packing, stop packing, dev commands.
            foreach (var gizmo in PackingTracker.GetGizmos())
            {
                yield return gizmo;
            }

            // 'Remove Pawn' gizmo.
            yield return new Command_Action
            {
                action = () =>
                {
                    var list = new List<FloatMenuOption>();
                    var isDocked = VisitorTracker.TryGetDockedCaravan(out var dockedCaravan);
                    foreach (var pawn in _occupants)
                    {
                        var label = pawn.Name.ToStringFull.CapitalizeFirst();
                        Action action;
                        if (!isDocked)
                        {
                            var wrappedPawn = Gen.YieldSingle(RemovePawn(pawn));
                            action = () => CaravanMaker.MakeCaravan(wrappedPawn, pawn.Faction, base.Tile, true);
                        }
                        else
                        {
                            action = () => dockedCaravan.AddPawn(pawn, true);
                        }
                        list.Add(new FloatMenuOption(label, action));
                    }
                    Find.WindowStack.Add(new FloatMenu(list));
                },
                defaultLabel = "Outposts.Commands.Remove.Label".Translate(),
                defaultDesc = "Outposts.Commands.Remove.Desc".Translate(),
                icon = Tex.RemoveTex,
                disabled = _occupants.Count == 1,
                disabledReason = "Outposts.Command.Remove.Only1".Translate()
            };

            // TODO: replace ProductionString().NullOrEmpty() with proper check.
            if (OutpostsMod.Settings.DeliveryMethod != DeliveryMethod.Store && !ProductionString().NullOrEmpty())
            {
                // Choose delivery colony.
                yield return new Command_Action
                {
                    action = () =>
                    {
                        var menuOptions = new List<FloatMenuOption>();
                        foreach (var map in this.GetAllPlayerHomes())
                        {
                            menuOptions.Add(new FloatMenuOption(map.Parent.LabelCap, () => DeliveryMap = map));
                        }
                        Find.WindowStack.Add(new FloatMenu(menuOptions));
                    },
                    defaultLabel = "Outposts.Commands.DeliveryColony.Label".Translate(),
                    defaultDesc = "Outposts.Commands.DeliveryColony.Desc".Translate(DeliveryMap?.Parent.LabelCap),
                    icon = SettleUtility.SettleCommandTex
                };
            }

            if (Prefs.DevMode)
            {
                // Debug '10 Damage random pawn' gizmo.
                yield return new Command_Action
                {
                    action = () =>
                    {
                        var dinfo = new DamageInfo(DamageDefOf.Crush, 10f);
                        dinfo.SetIgnoreInstantKillProtection(true);
                        _occupants.InnerListForReading.RandomElement().TakeDamage(dinfo);
                    },
                    defaultLabel = "Dev: Random pawn takes 10 damage"
                };

                // Debug 'All pawns 0% food' gizmo.
                yield return new Command_Action
                {
                    action = () =>
                    {
                        foreach (var pawn in _occupants)
                        {
                            pawn.needs.food.CurLevel = 0f;
                        }
                    },
                    defaultLabel = "Dev: All pawns 0% food"
                };
            }

            // Rename gizmo.
            yield return new Command_Action
            {
                icon = TexButton.Rename,
                defaultLabel = "Rename".Translate(),
                action = () => Find.WindowStack.Add(new Dialog_RenameOutpost(this))
            };
        }

        public override IEnumerable<Gizmo> GetCaravanGizmos(Caravan caravan)
        {
            // Inherited gizmos.
            foreach (var gizmo in base.GetCaravanGizmos(caravan))
            {
                yield return gizmo;
            }
            
            // 'Add Pawn' gizmo, for adding pawns from visiting caravan.
            yield return new Command_Action
            {
                action = () =>
                {
                    var list = new List<FloatMenuOption>();
                    foreach (var pawn in caravan.PawnsListForReading)
                    {
                        var report = Ext.CanAddPawn(pawn);
                        string label = report.Accepted ? pawn.NameFullColored.CapitalizeFirst().Resolve() : pawn.NameFullColored + " - " + report.Reason;
                        Action button = report.Accepted ? () => AddPawn(pawn) : null;
                        list.Add(new FloatMenuOption(label, button));
                    }
                    Find.WindowStack.Add(new FloatMenu(list));
                },
                defaultLabel = "Outposts.Commands.AddPawn.Label".Translate(),
                defaultDesc = "Outposts.Commands.AddPawn.Desc".Translate(),
                icon = Tex.AddTex
            };

            // 'Take Items' gizmo, for transferring items to a visiting caravan.
            yield return new Command_Action
            {
                action = () => Find.WindowStack.Add(new Dialog_TakeItems(this, caravan)),
                defaultLabel = "Outposts.Commands.TakeItems.Label".Translate(),
                defaultDesc = "Outposts.Commands.TakeItems.Desc".Translate(Name),
                icon = Tex.RemoveItemsTex
            };

            // 'Give Items' gizmo, for adding items from visiting caravan.
            yield return new Command_Action
            {
                action = () => Find.WindowStack.Add(new Dialog_GiveItems(this, caravan)),
                defaultLabel = "Outposts.Commands.GiveItems.Label".Translate(),
                defaultDesc = "Outposts.Commands.GiveItems.Desc".Translate(caravan.Name),
                icon = Tex.RemoveItemsTex
            };
        }

        

        /*
        public Pawn RemovePawn(Pawn p)
        {
            p.GetCaravan()?.RemovePawn(p);
            p.holdingOwner?.Remove(p);
            _occupants.Remove(p);
            RecachePawnTraits();
            return p;
        }
        */

        public override string GetInspectString() =>
            base.GetInspectString() +
            def.LabelCap.Line() +
            "Outposts.Contains".Translate(_occupants.Count).Line() +
            "Outposts.Packing".Translate(ticksTillPacked.ToStringTicksToPeriodVerbose().Colorize(ColoredText.DateTimeColor)).Line(Packing) +
            ProductionString().Line(!Packing) +
            RelevantSkillDisplay().Line(Ext?.RelevantSkills?.Count > 0);

        // TODO: make sure Pawn.Kill calls this
        public virtual void Notify_MemberDied(Pawn member)
        {
            if (!member.Dead)
            {
                Log.Error("Alive pawn reported as dead.");
                return;
            }

            if (!base.Spawned)
            {
                Log.Error("Outpost occupant died in unspawned outpost.");
            }

            // Turn dead member into corpse, put all non-locked items into outpost inventory.
            _occupants.Remove(member);
            this.StripOccupant(member);
            _inventory.TryAdd(member.Corpse);

            // If anyone else is left alive.
            var isAnyOtherOccupants = _occupants.InnerListForReading.Any(x => x != member);
            if (!isAnyOtherOccupants)
            {
                Abandon();
            }
        }

        public override void PostMapGenerate()
        {
            base.PostMapGenerate();

            var pawns = Map.mapPawns.AllPawns.Where(pawn => pawn.RaceProps.Humanlike || pawn.HostileTo(Faction));
            foreach (var pawn in pawns)
            {
                pawn.Destroy();
            }

            foreach (var occupant in _occupants)
            {
                GenPlace.TryPlaceThing(occupant, Map.Center, Map, ThingPlaceMode.Near);
                if (occupant.Position.Fogged(Map))
                {
                    FloodFillerFog.FloodUnfog(occupant.Position, Map);
                }
            }
        }

        public virtual void MapClearAndReset()
        {
            _occupants.Clear();
            var pawns = Map.mapPawns.AllPawns.ListFullCopy();
            foreach (var pawn in pawns)
            {
                if (pawn.Faction?.IsPlayer is true || pawn.HostFaction?.IsPlayer is true)
                {
                    pawn.DeSpawn();
                    _occupants.Add(pawn);
                }
            }
            RecachePawnTraits();
            RaidTracker.Reset();
        }

        public override bool ShouldRemoveMapNow(out bool alsoRemoveWorldObject)
        {
            if (!Map.mapPawns.FreeColonists.Any())
            {
                _occupants.Clear();
                Find.LetterStack.ReceiveLetter("Outposts.Letters.Lost.Label".Translate(), "Outposts.Letters.Lost.Text".Translate(Name),
                    LetterDefOf.NegativeEvent);
                alsoRemoveWorldObject = true;
                return true;
            }

            var pawns = Map.mapPawns.AllPawns.ListFullCopy();
            if (!pawns.Any(p => p.Faction == raidFaction && !p.Downed )) 
            {
                //This is what's required for gauntlet figured i'd put it after the initial if so we're not checking bunch of extra things per tick
                if (Map.listerThings.ThingsInGroup(ThingRequestGroup.ThingHolder).Any(x => x is Skyfaller)) 
                {
                    foreach (var thing in Map.listerThings.ThingsInGroup(ThingRequestGroup.ThingHolder).Where(x => x is Skyfaller))
                    {
                        var skyfaller = thing as Skyfaller;
                        if (skyfaller.Faction == raidFaction || skyfaller.innerContainer.Any(x => x.Faction == raidFaction))
                        {
                            alsoRemoveWorldObject = false;
                            return false;
                        }
                    }
                }
                if (Map.listerBuildings.allBuildingsNonColonist.Any(x => x.Faction == raidFaction)) //I think this would make it so you have to destroy hives as well for something like AA blackhvie raid
                {
                    alsoRemoveWorldObject = false;
                    return false;
                }
                _occupants.Clear();
                foreach (var pawn in pawns)
                {
                    if (pawn.Faction is { IsPlayer: true } || pawn.HostFaction is { IsPlayer: true })
                    {
                        pawn.DeSpawn();
                        _occupants.Add(pawn);
                    }
                }
                AddLoot(raidFaction,raidPoints,Map,out var loot);
                Find.LetterStack.ReceiveLetter("Outposts.Letters.BattleWon.Label".Translate(), "Outposts.Letters.BattleWon.Text".Translate(Name, loot),
                LetterDefOf.PositiveEvent,
                new LookTargets(Gen.YieldSingle(this)));

                RecachePawnTraits();
                raidFaction = null;
                raidPoints = 0;
                alsoRemoveWorldObject = false;
                return true;
            }

            alsoRemoveWorldObject = false;
            return false;
        }
        
        public virtual void AddLoot(Faction raidFaction,float raidPoints,Map map, out string letter)//made these passed for benefit of ambush
        {
            //looking at this if a colonist gets downed and dropped their weapon would they not lose their weapon?
            //Also can get raider's weapon
            letter = null;
            StringBuilder sb = new StringBuilder();
            float mv = 0f;
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon).ToList())
            {
                if (!_inventory.Contains(thing) && !thing.Position.Fogged(map)) //In case ancient dangers are possible in these maps
                {
                    mv += thing.MarketValue;
                    thing.DeSpawn();
                    _inventory.Add(thing);
                }
            }
            //Rescue colonist corpses. Cant let those funeral opportunities go to waste
            foreach (Corpse corpse in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).ToList())
            {
                if (corpse.InnerPawn?.Faction?.IsPlayer ?? false)
                {
                    sb.AppendLine("Outposts.Letters.BattleWon.Rescued".Translate(corpse.InnerPawn.NameFullColored));
                    corpse.DeSpawn();
                    _inventory.Add(corpse);
                    continue;
                }
                //NonUnoPinata means no weapons trying to safely add compat because i use it XD
                if (!corpse.InnerPawn?.equipment?.AllEquipmentListForReading.NullOrEmpty() ?? false)
                {
                    foreach (var thing in corpse.InnerPawn.equipment.AllEquipmentListForReading.ToList())
                    {
                        if (thing.def.IsWeapon)
                        {
                            corpse.InnerPawn.equipment.TryDropEquipment(thing, out var equipment, corpse.Position, false);
                            mv += equipment.MarketValue;
                            equipment.DeSpawn();
                            _inventory.Add(equipment);
                        }
                    }
                }
            }
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.Where(x => x.Faction == raidFaction && x.Downed).ToList())
            {
                if (Rand.Chance(0.33f) && !pawn.Dead && pawn.RaceProps.Humanlike)
                {
                    sb.AppendLine("Outposts.Letters.BattleWon.Captured".Translate(pawn.NameFullColored));
                    pawn.guest.CapturedBy(Faction);
                    pawn.DeSpawn();
                    AddPawn(pawn);
                }
            }
            //I wanted loot
            if (raidFaction.def.raidLootMaker != null)
            {
                float raidLootPoints = raidPoints * Find.Storyteller.difficulty.EffectiveRaidLootPointsFactor;
                float num = raidFaction.def.raidLootValueFromPointsCurve.Evaluate(raidLootPoints);
                ThingSetMakerParams parms2 = default(ThingSetMakerParams);
                parms2.totalMarketValueRange = new FloatRange(num, num);
                parms2.makingFaction = raidFaction;
                List<Thing> loot = raidFaction.def.raidLootMaker.root.Generate(parms2);                
                foreach (Thing thing in loot)
                {
                    mv += thing.MarketValue;
                    AddItem(thing);
                }                
            }
            if (mv > 0)
            {
                sb.AppendLine("Outposts.Letters.BattleWon.Secured".Translate(mv.ToStringMoney()));
            }
            if (sb.Length > 0)
            {
                letter = sb.ToString(); 
            }
        }

        public void Deliver(IEnumerable<Thing> items)
        {
            var things = items.ToList();
            var map = DeliveryMap ?? Find.Maps.Where(m => m.IsPlayerHome).OrderBy(m => Find.WorldGrid.ApproxDistanceInTiles(m.Parent.Tile, Tile)).FirstOrDefault();
            if (map == null) //chance of this is super low, but it's possible for those dumb enough to play nomads
            {
                Log.Warning("Vanilla Outpost Expanded Tried to deliver to a null map, storing instead");
                foreach (var item in things) _inventory.Add(item);
                return;
            }

            var text = "Outposts.Letters.Items.Text".Translate(Name) + "\n";
            var counts = new List<ThingDefCountClass>();

            var lookAt = new List<Thing>();

            var dir = Find.WorldGrid.GetRotFromTo(map.Parent.Tile, Tile);


            switch (OutpostsMod.Settings.DeliveryMethod)
            {
                case DeliveryMethod.Teleport:
                    {
                        IntVec3 cell;
                        if (map.listerBuildings.AllBuildingsColonistOfDef(Outposts_DefOf.VEF_OutpostDeliverySpot).TryRandomElement(out var spot))
                            cell = spot.Position;
                        else if (!CellFinder.TryFindRandomEdgeCellWith(x =>
                                         !x.Fogged(map) && x.Standable(map) &&
                                         map.mapPawns.FreeColonistsSpawned.Any(p => p.CanReach(x, PathEndMode.OnCell, Danger.Some)), map,
                                     dir, CellFinder.EdgeRoadChance_Always, out cell))
                            cell = CellFinder.RandomEdgeCell(dir, map);
                        foreach (var item in things) GenPlace.TryPlaceThing(item, cell, map, ThingPlaceMode.Near, (t, i) => lookAt.Add(t));

                        break;
                    }
                case DeliveryMethod.PackAnimal:
                    Deliver_PackAnimal(things, map, dir, lookAt);
                    break;
                case DeliveryMethod.Store:
                    foreach (var item in things) _inventory.Add(item);
                    break;
                case DeliveryMethod.ForcePods:
                    Deliver_Pods(things, map, lookAt);
                    break;
                case DeliveryMethod.PackOrPods:
                    if (Outposts_DefOf.TransportPod.IsFinished)
                        Deliver_Pods(things, map, lookAt);
                    else
                        Deliver_PackAnimal(things, map, dir, lookAt);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var singles = new List<Thing>();
            foreach (var item in things)
            {
                if (item.def.MadeFromStuff || (item.def.useHitPoints && item.HitPoints < item.MaxHitPoints) || item.TryGetQuality(out _))
                {
                    singles.Add(item);
                    continue;
                }

                var count = counts.Find(cc => cc.thingDef == item.def);
                if (count is null)
                {
                    count = new ThingDefCountClass { thingDef = item.def, count = 0 };
                    counts.Add(count);
                }

                count.count += item.stackCount;
            }

            foreach (var single in singles) text += "  - " + single.LabelCap + "\n";
            foreach (var count in counts) text += "  - " + count.Summary + "\n";

            Find.LetterStack.ReceiveLetter("Outposts.Letters.Items.Label".Translate(Name), text, LetterDefOf.PositiveEvent, new LookTargets(lookAt));
        }

        private static void Deliver_Pods(IEnumerable<Thing> items, Map map, List<Thing> lookAt)
        {
            IntVec3 cell;
            if (map.listerBuildings.AllBuildingsColonistOfDef(Outposts_DefOf.VEF_OutpostDeliverySpot).TryRandomElement(out var spot))
            {
                lookAt.Add(spot);
                cell = spot.Position;
            }
            else
                cell = DropCellFinder.TradeDropSpot(map);

            foreach (var item in items)
            {
                if (!DropCellFinder.TryFindDropSpotNear(cell, map, out var loc, false, false, false))
                    loc = DropCellFinder.RandomDropSpot(map);
                TradeUtility.SpawnDropPod(loc, map, item);
            }
        }

        private void Deliver_PackAnimal(IEnumerable<Thing> items, Map map, Rot4 dir, List<Thing> lookAt)
        {
            if (!Biome.AllWildAnimals.Where(x => x.RaceProps.packAnimal).TryRandomElement(out var pawnKind)) pawnKind = PawnKindDefOf.Muffalo;

            if (!CellFinder.TryFindRandomEdgeCellWith(x => !x.Fogged(map) && x.Standable(map), map,
                    dir, CellFinder.EdgeRoadChance_Always, out var cell) &&
                !RCellFinder.TryFindRandomPawnEntryCell(out cell, map, CellFinder.EdgeRoadChance_Always))
                cell = CellFinder.RandomEdgeCell(dir, map);

            var animal = PawnGenerator.GeneratePawn(pawnKind, Faction.OfPlayer);

            lookAt.Add(animal);

            foreach (var item in items) animal.inventory.TryAddItemNotForSale(item);

            GenSpawn.Spawn(animal, cell, map);

            IntVec3 deliverTo;
            if (
                map.listerBuildings.AllBuildingsColonistOfDef(Outposts_DefOf.VEF_OutpostDeliverySpot).TryRandomElement(out var spot))
                deliverTo = spot.Position;
            else if (!RCellFinder.TryFindRandomSpotJustOutsideColony(animal, out deliverTo))
                deliverTo = CellFinderLoose.RandomCellWith(x =>
                    !x.Fogged(map) && x.Standable(map) && animal.CanReach(x, PathEndMode.OnCell, Danger.Deadly), map);

            LordMaker.MakeNewLord(Faction.OfPlayer, new LordJob_Deliver(deliverTo), map, new[] { animal });
        }

        private static readonly SimpleCurve ThreatPointsOverPointsCurve = new() //A
    {
        new CurvePoint(35f, 38.5f),
        new CurvePoint(400f, 165f),
        new CurvePoint(10000f, 4125f)
    };

        private static readonly SimpleCurve ThreatPointsFactorOverPawnCountCurve = new() //B
    {
        new CurvePoint(1f, 0.5f),
        new CurvePoint(2f, 0.55f),
        new CurvePoint(5f, 1f),
        new CurvePoint(8f, 1.1f),
        new CurvePoint(20f, 2f)
    };

        private static readonly SimpleCurve ThreatPointsFactorOverLocalWealth = new() //C
    {
        new CurvePoint(1000f, 0.5f),
        new CurvePoint(24000f, 1f),
        new CurvePoint(50000f, 1.1f),
        new CurvePoint(250000f, 2f)
    };

        public virtual float WealthForCurve //using the map PlayerWealth is awkward because it requires the pawns to be spawned
        {
            get
            {
                var wealth = 0f;
                foreach (var pawn in AllPawns.Where(x => (x.RaceProps.Humanlike && !x.IsPrisoner) || x.training?.CanAssignToTrain(TrainableDefOf.Release).Accepted == true))
                {
                    wealth += WealthWatcher.GetEquipmentApparelAndInventoryWealth(pawn);
                    var marketValue = pawn.MarketValue;
                    if (pawn.IsSlave) marketValue *= 0.75f;
                    wealth += marketValue;
                }
                return wealth;
            }
        }

        public virtual SimpleCurve ThreatCurve => ThreatPointsOverPointsCurve;

        public virtual SimpleCurve PawnCurve => ThreatPointsFactorOverPawnCountCurve;

        public virtual SimpleCurve WealthCurve => ThreatPointsFactorOverLocalWealth;

        //Basic explination of raid points. Make it harder as main colonies wealth goes up
        //Based on 3 curves and a rand range.
        //A: Colony threat points -> base points
        //B: Factor based on # local of fighting colonists/animals
        //C: factor based on Local wealth
        //D: FloatRange that reduces further
        //(A*B*C)*D
        public virtual float ResolveRaidPoints(IncidentParms parms, float rangeMin = 0.25f, float rangeMax = 0.35f)
        {
            var pointFactorRange = new FloatRange(rangeMin, rangeMax);
            var mapPoints = HasMap ? StorytellerUtility.DefaultThreatPointsNow(parms.target) : 35f; //Min points 
            float fighters = AllPawns.Count(x =>
                (x.RaceProps.Humanlike && !x.IsPrisoner) ||
                (x.training?.CanAssignToTrain(TrainableDefOf.Release).Accepted ?? false)); //Humans and fighting animals           
            var points = ThreatCurve.Evaluate(parms.points) * PawnCurve.Evaluate(fighters) * WealthCurve.Evaluate(WealthForCurve);
            points *= pointFactorRange.RandomInRange;
            //Log.Message("PreMultiPoints," + points.ToString() + "," + mapPoints.ToString());
            points = Mathf.Max(points, mapPoints) * OutpostsMod.Settings.RaidDifficultyMultiplier;
            return Mathf.Clamp(points, 35f, 10000f); //I pity whoever makes it hit 10k via settings
        }

        //Debug to test impact of colony
        public void Debug(IncidentParms parms, float rangeMin = 0.25f, float rangeMax = 0.35f)
        {
            var origParm = parms.points;
            var mapPoints = StorytellerUtility.DefaultThreatPointsNow(parms.target); //Min points 
            Log.Message("Min: " + mapPoints);
            for (var i = 1; i < 100; i++)
            {
                parms.points = 100 * i;
                var points = ResolveRaidPoints(parms, rangeMin, rangeMax);
                Log.Message("Colony Points: " + parms.points + "," + points);
            }

            parms.points = origParm;
        }

        private readonly Dictionary<SkillDef, int> totalSkills = new();

        public int TotalSkill(SkillDef skill)
        {
            if (_skillsDirty)
            {
                foreach (var skillDef in DefDatabase<SkillDef>.AllDefs)
                {
                    totalSkills[skillDef] = CapablePawns.Sum(p => p.skills.GetSkill(skill).Level);
                }
            }

            return totalSkills[skill];
        }

        protected virtual bool IsCapable(Pawn pawn)
        {
            if (!pawn.RaceProps.Humanlike || pawn.skills is null)
            {
                return false;
            }

            return !Ext.RelevantSkills.Any(skill => pawn.skills.GetSkill(skill).TotallyDisabled);
        }

        public bool Has(Pawn pawn) => _occupants.Contains(pawn);

        // Theres a few things that seem to leave 0 or destroyed things in contained.
        // I fixed tend, I think the other ones is stuff decaying maybe? Not sure typically its food stuff doing it.
        public void CheckNoDestroyedOrNoStack()
        {
            foreach (var item in _inventory.Where(thing => thing.Destroyed || thing.stackCount == 0))
            {
                _inventory.Remove(item);
            }
        }

        public virtual string ProductionString()
        {
            var options = ResultOptions;
            if (Ext is null || options is not { Count: > 0 }) return "";
            return options.Count switch
            {
                1 => "Outposts.WillProduce.1".Translate(options[0].Amount(CapablePawns.ToList()), options[0].Thing.label, TimeTillProduction).RawText,
                2 => "Outposts.WillProduce.2".Translate(options[0].Amount(CapablePawns.ToList()), options[0].Thing.label, options[1].Amount(CapablePawns.ToList()),
                    options[1].Thing.label, TimeTillProduction).RawText,
                _ => "Outposts.WillProduce.N".Translate(TimeTillProduction, options.Select(ro => ro.Explain(CapablePawns.ToList())).ToLineList("  - ")).RawText
            };
        }

        public virtual string RelevantSkillDisplay() =>
            Ext.RelevantSkills
            .Select(skill => "Outposts.TotalSkill".Translate(skill.skillLabel, TotalSkill(skill)).RawText)
            .ToLineList();

        //Profiled with 7 outposts 62 pawns
        //my "light" health tick Max 0.308 avg 0.122
        //pawn Health Tick 0.344 max avg 0.152
        //Just wounds 0.201 max avg 0.065
        //Light vs Health tick makes marginal difference. Though I might still leave it because health tick will cause disease traits which unesecarry and does have a bit of bloat
        //With Just wounds people are basically in stasis which leads to awkward situations. Like a pawn I had who had anesthia woozy still a year later when it got raided
        //In my mind the impact of performance is small enough that the "realism" is worth it.
        public virtual void OutpostHealthTick(Pawn pawn)
        {
            if (pawn.health?.hediffSet == null || pawn.Dead) return; //Just in case the birthday killed them XD

            var removedAnything = false;
            var health = pawn.health;
            //Hediff ticks and immunes
            for (var index = health.hediffSet.hediffs.Count - 1; index >= 0; index--)
            {
                var hediff = health.hediffSet.hediffs[index];
                try
                {
                    hediff.Tick();
                    hediff.PostTick();
                }
                catch
                {
                    health.RemoveHediff(hediff);
                }

                if (pawn.Dead) return;
                if (hediff.ShouldRemove)
                {
                    health.hediffSet.hediffs.RemoveAt(index);
                    hediff.PostRemoved();
                    removedAnything = true;
                }
            }

            if (removedAnything)
            {
                health.Notify_HediffChanged(null);
                removedAnything = false;
            }

            Utils.ImmunityTickDelegate(health.immunity);

            //Tend and injuries
            //Changed interval to 600 as thats what health tick is, and ppl seemed to heal super fast
            if (!pawn.IsHashIntervalTick(600)) return;

            if (pawn.health.HasHediffsNeedingTend())
            {
                var doctor = AllPawns.Where(p => p.RaceProps.Humanlike && !p.Downed).MaxBy(p => p.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? -1f);
                if (doctor != null)
                {
                    Medicine medicine = null;
                    var potency = 0f;
                    CheckNoDestroyedOrNoStack();
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

        public override void Abandon()
        {
            Find.LetterStack.ReceiveLetter("Outposts.Abandoned".Translate(),
                                           "Outposts.Abandoned.Desc".Translate(Name),
                                           LetterDefOf.NegativeEvent);

            // TODO: Notify_AbandonedAtTile for all items in inventory

            base.Abandon();
        }
    }
}