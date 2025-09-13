using HarmonyLib;
using MineralsFramework;
using MineralsRock;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
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
            ThingDef rockDef = DefDatabase<ThingDef>.GetNamedSilentFail($"Passable{rockName}");
            
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

        static AutoGenerateRockReplacements()
        {
            Log.Message("Starting automatic rock replacement generation");

            int generatedCount = 0;
            // Cache all defs first to prevent modification during enumeration
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs.ToList())
            {
                if (def.building == null || !def.building.isNaturalRock || def.thingClass == typeof(StaticMineral))
                    continue;

                // Check if already replaced
                bool alreadyReplaced = DefDatabase<ThingDef>.AllDefs
                    .OfType<ThingDef_StaticMineral>()
                    .Any(sm => sm.ThingsToReplace != null && sm.ThingsToReplace.Contains(def.defName));

                if (alreadyReplaced)
                    continue;

                // Create new weathered def
                ThingDef_StaticMineral weathedDef = ThingDef_StaticMineral.MakeDefaultWeatheredRockDef();
                weathedDef.defName = "MF_Weathered" + def.defName;
                weathedDef.label = "Weathered " + def.label;
                weathedDef.description = def.description;
                weathedDef.graphicData.color = def.graphicData.color;
                weathedDef.building.mineableThing = def.building.mineableThing;
                weathedDef.building.mineableDropChance = def.building.mineableDropChance;
                weathedDef.ThingsToReplace = new List<string> { def.defName };
                weathedDef.allowedTerrains = new List<string> { def.defName + "_Rough" };
                weathedDef.neededNearbyTerrains = new List<string> { def.defName + "_Rough" };
                DefDatabase<ThingDef_StaticMineral>.Add(weathedDef);

                // Create new hewn def
                ThingDef_StaticMineral hewnDef = ThingDef_StaticMineral.MakeDefaultHewnRockDef();
                hewnDef.defName = "MF_Hewn" + def.defName;
                hewnDef.label = "Hewn " + def.label;
                hewnDef.description = def.description;
                hewnDef.graphicData.color = def.graphicData.color;
                hewnDef.building.mineableThing = def.building.mineableThing;
                hewnDef.building.mineableDropChance = def.building.mineableDropChance;
                hewnDef.ThingsToReplace = new List<string> { def.defName };
                hewnDef.allowedTerrains = new List<string> { def.defName + "_Rough" };
                hewnDef.neededNearbyTerrains = new List<string> { def.defName + "_Rough" };
                DefDatabase<ThingDef_StaticMineral>.Add(hewnDef);

                // Create new solid def
                ThingDef_StaticMineral solidDef = ThingDef_StaticMineral.MakeDefaultSolidRockDef();
                solidDef.defName = "MF_Solid" + def.defName;
                solidDef.label = "Solid " + def.label;
                solidDef.description = def.description;
                solidDef.graphicData.color = def.graphicData.color;
                solidDef.building.mineableThing = def.building.mineableThing;
                solidDef.building.mineableDropChance = def.building.mineableDropChance;
                solidDef.ThingsToReplace = new List<string> { def.defName };
                solidDef.allowedTerrains = new List<string> { def.defName + "_Rough" };
                solidDef.neededNearbyTerrains = new List<string> { def.defName + "_Rough" };
                DefDatabase<ThingDef_StaticMineral>.Add(solidDef);


                generatedCount++;
                Log.Message($"MineralsFramework: Auto-generated replacement rocks for {def.defName}");
            }
            
            Log.Message($"Generated {generatedCount} automatic rock replacement defs");
        }
    }
}
