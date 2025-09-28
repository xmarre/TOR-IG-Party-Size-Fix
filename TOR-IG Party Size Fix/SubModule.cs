using System;
using HarmonyLib;
using TaleWorlds.MountAndBlade;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace TOR_IG_PartyLimitFix
{
    public sealed class SubModule : MBSubModuleBase
    {
        private Harmony _h;
        private const string HID = "tor.ig.partylimit.fix";

        protected override void OnSubModuleLoad()
        {
            _h = new Harmony(HID);

            // Capture model instances as they register. Safe Prefix (no ref/out).
            var addModel = AccessTools.Method(typeof(CampaignGameStarter), "AddModel", new Type[] { typeof(GameModel) });
            _h.Patch(addModel, prefix: new HarmonyMethod(typeof(IGTorMergePatch).GetMethod(nameof(IGTorMergePatch.AddModel_Prefix))));
        }

        protected override void OnSubModuleUnloaded()
        {
            _h?.UnpatchAll(HID);
        }
    }
}
