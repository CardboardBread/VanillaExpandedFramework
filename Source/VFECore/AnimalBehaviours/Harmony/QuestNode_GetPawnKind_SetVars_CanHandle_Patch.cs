﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld.QuestGen;

namespace AnimalBehaviours
{




    [HarmonyPatch]
   
    public static class VanillaExpandedFramework_QuestNode_GetPawnKind_SetVars_CanHandle_Patch
    {
        static HashSet<PawnKindDef> animalListResult = new HashSet<PawnKindDef>();

        static VanillaExpandedFramework_QuestNode_GetPawnKind_SetVars_CanHandle_Patch()
        {

            HashSet<AnimalsDisabledFromQuestsDef> allUnaffectedLists = DefDatabase<AnimalsDisabledFromQuestsDef>.AllDefsListForReading.ToHashSet();
            foreach (AnimalsDisabledFromQuestsDef individualList in allUnaffectedLists)
            {
                animalListResult.AddRange(individualList.disabledFromQuestsPawns);
            }
        }


        static MethodBase TargetMethod()
        {
            MethodBase method = typeof(QuestNode_GetPawnKind).GetNestedType("<>c__DisplayClass5_1", BindingFlags.Instance | BindingFlags.NonPublic).GetMethod("<SetVars>g__CanHandle|1", BindingFlags.Instance | BindingFlags.NonPublic);
            return method;
        }

        public static void Postfix(PawnKindDef animal, ref bool __result)
        {
            
           
                if (animalListResult.Contains(animal))
                {
                    __result = false;
                }
            
            

        }

    }





}
