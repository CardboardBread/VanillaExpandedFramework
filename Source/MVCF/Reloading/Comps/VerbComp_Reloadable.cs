﻿using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MVCF.Commands;
using MVCF.Comps;
using MVCF.Utilities;
using MVCF.VerbComps;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace MVCF.Reloading.Comps;

public class VerbComp_Reloadable : VerbComp
{
    private static readonly AccessTools.FieldRef<Verb, int> burstShotsLeft = AccessTools.FieldRefAccess<Verb, int>("burstShotsLeft");
    public int ShotsRemaining;
    public VerbCompProperties_Reloadable Props => props as VerbCompProperties_Reloadable;

    public Thing ReloadItemInInventory => Pawn?.inventory?.innerContainer?.FirstOrDefault(CanReloadFrom);
    public Thing NewWeapon => Pawn?.inventory?.innerContainer?.FirstOrDefault(t => t.def.IsWeapon && t.def.equipmentType == EquipmentType.Primary);
    private Pawn Pawn => parent?.Manager?.Pawn;

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref ShotsRemaining, "shotsRemaining");
    }

    public override IEnumerable<CommandPart> GetCommandParts(Command_VerbTargetExtended command)
    {
        yield return new CommandPart_Reloadable
        {
            parent = command,
            Reloadable = this
        };
    }

    public virtual Thing Reload(Thing ammo)
    {
        if (!CanReloadFrom(ammo)) return null;
        var shotsToFill = ShotsToReload(ammo);
        ShotsRemaining += shotsToFill;
        return ammo.SplitOff(shotsToFill * Props.ItemsPerShot);
    }

    public virtual int ReloadTicks(Thing ammo) => ammo == null ? 0 : (Props.ReloadTimePerShot * ShotsToReload(ammo)).SecondsToTicks();
    private int ShotsToReload(Thing ammo) => Math.Min(ammo.stackCount / Props.ItemsPerShot, Props.MaxShots - ShotsRemaining);

    public virtual bool NeedsReload() => ShotsRemaining < Props.MaxShots;

    public virtual bool CanReloadFrom(Thing ammo)
    {
        if (ammo == null) return false;
        return Props.AmmoFilter.Allows(ammo) && ammo.stackCount >= Props.ItemsPerShot;
    }

    public virtual void Unload()
    {
        var thing = ThingMaker.MakeThing(Props.AmmoFilter.AnyAllowedDef);
        thing.stackCount = ShotsRemaining;
        ShotsRemaining = 0;
        var parentThing = parent.ParentThing();
        GenPlace.TryPlaceThing(thing, parentThing.PositionHeld, parentThing.MapHeld, ThingPlaceMode.Near);
    }

    public virtual void ReloadEffect(int curTick, int ticksTillDone)
    {
        if (curTick == ticksTillDone - 2f.SecondsToTicks()) Props.ReloadSound?.PlayOneShot(parent.Verb.caster);
    }

    public override void Initialize(VerbCompProperties props)
    {
        base.Initialize(props);
        ShotsRemaining = Props.StartLoaded ? Props.MaxShots : 0;
    }

    public override void Notify_ShotFired()
    {
        base.Notify_ShotFired();
        ShotsRemaining--;
        if (Pawn?.CurJobDef == JobDefOf.Hunt) Pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
    }

    public override bool Available() => ShotsRemaining >= (parent.Verb.Bursting ? burstShotsLeft(parent.Verb) : parent.Verb.verbProps.burstShotCount);
}

public class CommandPart_Reloadable : CommandPart
{
    public VerbComp_Reloadable Reloadable;

    public override void PostInit()
    {
        base.PostInit();
        if (Reloadable.ShotsRemaining < parent.verb.verbProps.burstShotCount)
            parent.Disable("CommandReload_NoAmmo".Translate("ammo".Named("CHARGENOUN"),
                Reloadable.Props.AmmoFilter.AnyAllowedDef.Named("AMMO"),
                ((Reloadable.Props.MaxShots - Reloadable.ShotsRemaining) * Reloadable.Props.ItemsPerShot).Named("COUNT")));
    }

    public override void ModifyInfo(ref string label, ref string topRightLabel, ref string desc, ref Texture icon)
    {
        base.ModifyInfo(ref label, ref topRightLabel, ref desc, ref icon);
        topRightLabel = Reloadable.ShotsRemaining + " / " + Reloadable.Props.MaxShots;
    }

    public override IEnumerable<FloatMenuOption> GetRightClickOptions()
    {
        if (Reloadable is VerbComp_Reloadable_ChangeableAmmo ccwa)
            foreach (var option in ccwa.AmmoOptions.Select(pair =>
                         new FloatMenuOption(pair.First.LabelCap, pair.Second)))
                yield return option;
    }
}

public class VerbCompProperties_Reloadable : VerbCompProperties
{
    public ThingFilter AmmoFilter;
    public List<ThingDefCountRangeClass> GenerateAmmo;
    public bool GenerateBackupWeapon;
    public int ItemsPerShot;
    public int MaxShots;
    public Type NewVerbClass;
    public SoundDef ReloadSound;
    public float ReloadTimePerShot;
    public bool StartLoaded = true;

    public override void ResolveReferences()
    {
        base.ResolveReferences();
        AmmoFilter.ResolveReferences();
    }

    public override void PostLoadSpecial(VerbProperties verbProps, AdditionalVerbProps additionalProps)
    {
        base.PostLoadSpecial(verbProps, additionalProps);
        MVCF.EnabledFeatures.Add("Reloading");
        MVCF.EnabledFeatures.Add("VerbComps");
        MVCF.EnabledFeatures.Add("ExtraEquipmentVerbs");
        ref var type = ref verbProps.verbClass;
        if (NewVerbClass != null) type = NewVerbClass;
    }
}