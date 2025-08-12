using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;

namespace MineralsRock
{
    class MineralsRockMain : Mod
    {
        public static MineralsRockSettings Settings;

        public MineralsRockMain(ModContentPack content) : base(content)
        {
            Settings = GetSettings<MineralsRockSettings>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.DoWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "MineralsRock";
        }
    }

}
