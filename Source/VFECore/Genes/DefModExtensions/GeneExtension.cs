﻿using RimWorld;
using System.Collections.Generic;
using Verse;

namespace VanillaGenesExpanded
{

    public class GeneExtension : DefModExtension
    {

        //Custom gene baclgrounds
        public string backgroundPathEndogenes;
        public string backgroundPathXenogenes;

        //Applies hediffs to existing body parts when the gene is acquired
        public List<HediffToBodyparts> hediffsToBodyParts;
        //Applies a hediff to the whole body when the gene is acquired
        public HediffDef hediffToWholeBody;

        //Makes "fur" associated with this gene use the body's colour, instead of the hair
        public bool useSkinColorForFur = false;

        //Reference to a thingsetmaker that makes pawns with this gene start play with some things (only on game start)
        public ThingSetMakerDef thingSetMaker = null;

        //Gender bender
        public bool forceMale = false;
        public bool forceFemale = false;

        public BodyTypeDef forcedBodyType;
        public string bodyNakedGraphicPath;
        public string bodyDessicatedGraphicPath;
        public string headDessicatedGraphicPath;
        public string skullGraphicPath;
    }

}