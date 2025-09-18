using HarmonyLib;
using MineralsFramework;
using MineralsRock;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace MineralsRock
{
    [HarmonyPatch(typeof(ThingSetMaker_Meteorite), "Reset")]
    public static class ThingSetMaker_Meteorite_Reset_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingSetMaker_Meteorite __instance)
        {
            // Modify the static nonSmoothedMineables list after reset
            var filteredMineables = (DefDatabase<ThingDef>.AllDefsListForReading.Where((ThingDef x) => x.mineable && !x.building.mineablePreventMeteorite && !x.IsSmoothed &&
typeof(MineralsFramework.ThingDef_StaticMineral).IsAssignableFrom(x.GetType()))).ToList();

            ThingSetMaker_Meteorite.nonSmoothedMineables = filteredMineables;

            if (MineralsFrameworkMain.Settings.debugModeEnabled)
            {
                Log.Message($"Minerals: Replaced meteorite mineables with {filteredMineables.Count} MineralsFramework defs");
            }
        }
    }


    [HarmonyPatch(typeof(ThingSetMaker_Meteorite), "FindRandomMineableDef")]
    public static class ThingSetMaker_Meteorite_FindRandomMineableDef_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref ThingDef __result)
        {
            // Completely replace original logic with simple random selection
            if (ThingSetMaker_Meteorite.nonSmoothedMineables.Count == 0)
                return true;
            
            __result = ThingSetMaker_Meteorite.nonSmoothedMineables.RandomElement();
            
            return false; // Skip original method
        }
    }

    [HarmonyPatch(typeof(Skyfaller), "Impact")]
    public static class Skyfaller_Impact_Patch
    {
        [HarmonyPrefix]
        public static void Postfix(Skyfaller __instance)
        {
            if (MineralsFrameworkMain.Settings.debugModeEnabled)
            {
                Log.Message($"MineralsRock: attempting to make skyfaller rock ring.");
            }
            
            if (__instance.innerContainer.Count < 3)
            {
                if (MineralsFrameworkMain.Settings.debugModeEnabled)
                {
                    Log.Message($"MineralsRock: to small of a skyfaller to make ring.");
                }
                return;
            }

            // Get nearest rocky terrain
            IntVec3 center = __instance.Position;
            TerrainDef nearestRockTerrain = null;
            int maxDist = 25;
            
            for (int x = center.x - maxDist; x <= center.x + maxDist; x++)
            {
                for (int z = center.z - maxDist; z <= center.z + maxDist; z++)
                {
                    IntVec3 pos = new IntVec3(x, 0, z);
                    if (pos.InBounds(__instance.Map))
                    {
                        TerrainDef terrain = __instance.Map.terrainGrid.TerrainAt(pos);
                        if (terrain != null && terrain.IsRock)
                        {
                            nearestRockTerrain = terrain;
                            break;
                        }
                    }
                }
                if (nearestRockTerrain != null) break;
            }

            if (nearestRockTerrain == null)
                return;

            // Try to find matching passable rock def (e.g. "Granite_Rough" -> "PassableGranite")
            string rockName = nearestRockTerrain.defName.Split('_')[0];
            ThingDef rockDef = DefDatabase<ThingDef>.GetNamedSilentFail($"Small{rockName}");
            
            if (rockDef == null)
            {
                if (MineralsFrameworkMain.Settings.debugModeEnabled)
                {
                    Log.Message($"MineralsRock: cant find nearby terrain to make ring.");
                }
                return;
            }

            // Calculate ring size based on item count
            int itemCount = __instance.innerContainer.Count;
            int radius = (int)(GenMath.Sqrt(itemCount) * 2f);
            if (radius < 2)
            {
                radius = 2;
            }

            // Generate hollow ring pattern
            Log.Message($"MineralsRock: center: {center}.");
            foreach (IntVec3 offset in GenRadial.RadialPatternInRadius(radius))
            {
                Log.Message($"MineralsRock: offset: {offset}.");
                // Only place rocks in outer ring
                float distance = (float)Math.Sqrt(offset.x * offset.x + offset.y * offset.y + offset.z * offset.z);
                float rockSize = (0.33f - (radius - distance) / radius) * 3 + ((float)Rand.Range(-7, 2) / 10f);
                Log.Message($"MineralsRock: distance: {distance}.");
                if (rockSize < 0.05f)
                    continue;
                if (rockSize > 1f)
                    rockSize = 1;

                IntVec3 targetPos = center + offset;
                if (targetPos.InBounds(__instance.Map) && 
                    targetPos.GetEdifice(__instance.Map) == null && 
                    GenSight.LineOfSight(center, targetPos, __instance.Map))
                {
                    MineralsFramework.StaticMineral spawned = (MineralsFramework.StaticMineral) GenSpawn.Spawn(rockDef, targetPos, __instance.Map);
                    spawned.size = rockSize;
                }
            }

            if (MineralsFrameworkMain.Settings.debugModeEnabled)
            {
                Log.Message($"Generated {rockDef.defName} ring with radius {radius} around {center}");
            }
        }
    }

    // Auto-generate StaticMineral defs for unreplaced natural rocks
    [StaticConstructorOnStartup]
    public static class AutoGenerateRockReplacements
    {
        private static List<string> ProcessTemplates()
        {
            List<string> replacedDefs = new List<string>();
            foreach (ThingDef_StaticMineral templateDef in DefDatabase<ThingDef_StaticMineral>.AllDefs.ToList())
            {
                if (templateDef.isTemplateFor == null || templateDef.isTemplateFor.Count == 0 || string.IsNullOrEmpty(templateDef.templateReplaceString))
                    continue;

                foreach (string targetDefname in templateDef.isTemplateFor)
                {

                    // If def to be created already exists then dont try to create
                    string newDefname = templateDef.defName.Replace(templateDef.templateReplaceString, targetDefname);
                    if (DefDatabase<ThingDef_StaticMineral>.GetNamedSilentFail(newDefname) != null)
                    {
                        continue;
                    }

                    // If target def does not exist then dont try to create
                    ThingDef targetDef = DefDatabase<ThingDef>.GetNamedSilentFail(targetDefname);
                    if (targetDef == null)
                    {
                        continue;
                    }

                    // Create deep copy and replace template strings
                    ThingDef_StaticMineral clone = templateDef.DeepCopy();
                    clone.defName = newDefname;
                    clone.label = templateDef.label?.Replace(templateDef.templateReplaceString, targetDef.label);
                    clone.description = targetDef.description;
                    if (targetDef.building != null)
                    {
                        if (clone.building == null)
                        {
                            clone.building = new BuildingProperties();
                        }
                        if (targetDef.building.mineableThing != null)
                        {
                            if (targetDef.building.isResourceRock && targetDef.building.mineableYield >= 3)
                            {
                                clone.randomlyDropResources.Add(new RandomResourceDrop()
                                {
                                    ResourceDefName = targetDef.building.mineableThing.defName,
                                    DropProbability = 3,
                                    CountPerDrop = (int)(Math.Ceiling((float)targetDef.building.mineableYield / 3f))

                                });
                            } else
                            {
                                clone.building.mineableThing = targetDef.building.mineableThing;
                                clone.building.mineableDropChance = targetDef.building.mineableDropChance;
                                clone.building.mineableYield = targetDef.building.mineableYield;
                            }
                                
                        }
                        
                    }
                    clone.ThingsToReplace = templateDef.ThingsToReplace?
                        .Select(s => s.Replace(templateDef.templateReplaceString, targetDefname))
                        .ToList();
                    clone.allowedTerrains = templateDef.allowedTerrains?
                        .Select(s => s.Replace(templateDef.templateReplaceString, targetDefname))
                        .ToList();
                    clone.associatedOres = templateDef.associatedOres?
                        .Select(s => s.Replace(templateDef.templateReplaceString, targetDefname))
                        .ToList();
                    clone.neededNearbyTerrains = templateDef.neededNearbyTerrains?
                        .Select(s => s.Replace(templateDef.templateReplaceString, targetDefname))
                        .ToList();
                    if (targetDef.graphicData != null)
                    {
                        if (clone.graphicData == null)
                        {
                            clone.graphicData = new GraphicData();
                        }
                        if (targetDef.graphicData.color != null)
                        {
                            clone.graphicData.color = targetDef.graphicData.color;
                            clone.randomColorsOne = new List<Color>() { targetDef.graphicData.color };
                        }
                        if (targetDef.graphicData.colorTwo == null)
                        {
                            if (clone.graphicData.colorTwo == null)
                            {
                                clone.graphicData.colorTwo = targetDef.graphicData.color;
                            }
                        } else
                        {
                            clone.graphicData.colorTwo = targetDef.graphicData.colorTwo;
                            clone.randomColorsTwo = new List<Color>() { targetDef.graphicData.colorTwo };
                        }
                    }
                    float targetMaxHitpoints = targetDef.GetStatValueAbstract(StatDefOf.MaxHitPoints);
                    float templateMaxHitpoints = templateDef.GetStatValueAbstract(StatDefOf.MaxHitPoints);
                    clone.SetStatBaseValue(StatDefOf.MaxHitPoints, targetMaxHitpoints * templateMaxHitpoints / 1100);
                    clone.templateReplaceString = null;
                    clone.isTemplateFor = null;

                    if (MineralsFrameworkMain.Settings.debugModeEnabled)
                    {
                        Log.Message($"MineralsRock: Generated '{clone.defName}' for '{targetDef.defName}' based on '{templateDef.defName}' template.");
                    }

                    DefDatabase<ThingDef_StaticMineral>.Add(clone);

                    if (! replacedDefs.Contains(targetDef.defName))
                    {
                        replacedDefs.Add(targetDef.defName);
                    }
                }
            }
            return replacedDefs;
        }

        static AutoGenerateRockReplacements()
        {
            // Generate rock/ore defs from templates defined by defname in the XML
            List<string> replacedWithDefined = ProcessTemplates();
            Log.Message($"MineralsRock: Automatically generated rock defs for {replacedWithDefined.Count} modded rocks based on defined templates: {replacedWithDefined.Join<string>()}.");

            // Add rocks/ores without a corresponding ThingDef_StaticMineral to the generic rock/ore's isTemplateFor
            List<ThingDef_StaticMineral> genericRockDefs = DefDatabase<ThingDef_StaticMineral>.AllDefs
                .Where(def => def.tags != null && def.tags.Contains("generic_rock_template"))
                .ToList();
            List<ThingDef_StaticMineral> generiOreDefs = DefDatabase<ThingDef_StaticMineral>.AllDefs
                .Where(def => def.tags != null && def.tags.Contains("generic_ore_template"))
                .ToList();
            List<string> allReplacedThings = DefDatabase<ThingDef_StaticMineral>.AllDefs
                .Where(def => def.ThingsToReplace != null)
                .SelectMany(def => def.ThingsToReplace)
                .Distinct()
                .ToList();
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
            {
                // Check if it looks like a vanilla rock/ore
                if (def.building == null || !def.building.isNaturalRock || def.thingClass == typeof(StaticMineral))
                    continue;

                // Check if there is already a ThingDef_StaticMineral that replaces it
                if (allReplacedThings.Contains(def.defName))
                    continue;

                // If it is an ore, add its defname to all generic ore templates, otherwise assume it is a rock
                if (def.building.isResourceRock)
                {
                    foreach (ThingDef_StaticMineral oreDef in generiOreDefs)
                    {
                        oreDef.isTemplateFor.Add(def.defName);
                    }
                }
                else
                {
                    foreach (ThingDef_StaticMineral rockDef in genericRockDefs)
                    {
                        rockDef.isTemplateFor.Add(def.defName);
                    }
                }
            }

            // Check for new targets added to the generic templates
            List<string> replacedWithGeneric = ProcessTemplates();
            Log.Warning($"MineralsRock: Automatically generated rock defs for {replacedWithGeneric.Count} modded rocks based on GENERIC templates: {replacedWithGeneric.Join<string>()}.");

        }
    }
}
