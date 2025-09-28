using HarmonyLib;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library; // MathF
using TaleWorlds.Localization;

namespace TOR_IG_PartyLimitFix
{
    internal static class MergeState
    {
        public static PartySizeLimitModel LastNonIG; // the real TOR/other instance seen most recently
        public static PartySizeLimitModel Vanilla;   // baseline instance
        public static Type IGType;                   // IG model type
        public static bool Patched;
        public const string HID = "tor.ig.partylimit.fix";
    }

    internal static class IGTorMergePatch
    {
        // Safe Prefix: only observe and arm patches once IG model type is known.
        public static void AddModel_Prefix(GameModel model)
        {
            try
            {
                var psl = model as PartySizeLimitModel;
                if (psl == null) return;

                // Baseline once
                if (MergeState.Vanilla == null)
                    MergeState.Vanilla = MakeVanilla() ?? psl;

                // IG detection by assembly/name
                var asm = model.GetType().Assembly.GetName().Name ?? string.Empty;
                var low = asm.ToLowerInvariant();
                bool isIG = low.Contains("improved") && low.Contains("garr");

                if (!isIG)
                {
                    // Track the actual live TOR/other instance (not a new one)
                    MergeState.LastNonIG = psl;
                    return;
                }

                // Arm IG patches once we know the concrete IG type
                if (!MergeState.Patched)
                {
                    MergeState.IGType = model.GetType();
                    var h = new Harmony(MergeState.HID);

                    var m1 = AccessTools.Method(MergeState.IGType, "GetPartyMemberSizeLimit", new Type[] { typeof(PartyBase), typeof(bool) });
                    var m2 = AccessTools.Method(MergeState.IGType, "GetPartyPrisonerSizeLimit", new Type[] { typeof(PartyBase), typeof(bool) });
                    var m3 = AccessTools.Method(MergeState.IGType, "GetTierPartySizeEffect", new Type[] { typeof(int) });
                    var m4 = AccessTools.Method(MergeState.IGType, "GetAssumedPartySizeForLordParty", new Type[] { typeof(Hero), typeof(IFaction), typeof(Clan) });

                    if (m1 != null) h.Patch(m1, postfix: new HarmonyMethod(typeof(IGTorMergePatch).GetMethod(nameof(Post_Member))));
                    if (m2 != null) h.Patch(m2, postfix: new HarmonyMethod(typeof(IGTorMergePatch).GetMethod(nameof(Post_Prisoner))));
                    if (m3 != null) h.Patch(m3, postfix: new HarmonyMethod(typeof(IGTorMergePatch).GetMethod(nameof(Post_Tier))));
                    if (m4 != null) h.Patch(m4, postfix: new HarmonyMethod(typeof(IGTorMergePatch).GetMethod(nameof(Post_Assumed))));

                    MergeState.Patched = true;
                }
            }
            catch { }
        }

        // === Postfixes: add delta from the captured TOR/other model over vanilla ===
        public static void Post_Member(PartyBase party, bool includeDescriptions, ref ExplainedNumber __result)
        {
            try
            {
                var tor = MergeState.LastNonIG;
                var v = MergeState.Vanilla;
                if (tor == null || v == null) return;

                var e0 = v.GetPartyMemberSizeLimit(party, includeDescriptions);
                var et = tor.GetPartyMemberSizeLimit(party, includeDescriptions);
                float d = et.ResultNumber - e0.ResultNumber;
                if (MathF.Abs(d) > 1e-4f)
                    __result.Add(d, new TextObject("{=tor_delta_restore}TOR/Other party bonuses"));
            }
            catch { }
        }

        public static void Post_Prisoner(PartyBase party, bool includeDescriptions, ref ExplainedNumber __result)
        {
            try
            {
                var tor = MergeState.LastNonIG;
                var v = MergeState.Vanilla;
                if (tor == null || v == null) return;

                var e0 = v.GetPartyPrisonerSizeLimit(party, includeDescriptions);
                var et = tor.GetPartyPrisonerSizeLimit(party, includeDescriptions);
                float d = et.ResultNumber - e0.ResultNumber;
                if (MathF.Abs(d) > 1e-4f)
                    __result.Add(d, new TextObject("{=tor_delta_restore_pris}TOR/Other prisoner bonuses"));
            }
            catch { }
        }

        public static void Post_Tier(int clanTier, ref int __result)
        {
            try
            {
                var tor = MergeState.LastNonIG;
                var v = MergeState.Vanilla;
                if (tor == null || v == null) return;

                int d = tor.GetTierPartySizeEffect(clanTier) - v.GetTierPartySizeEffect(clanTier);
                if (d != 0) __result += d;
            }
            catch { }
        }

        public static void Post_Assumed(Hero hero, IFaction faction, Clan clan, ref int __result)
        {
            try
            {
                var tor = MergeState.LastNonIG;
                var v = MergeState.Vanilla;
                if (tor == null || v == null) return;

                int d = tor.GetAssumedPartySizeForLordParty(hero, faction, clan)
                        - v.GetAssumedPartySizeForLordParty(hero, faction, clan);
                if (d != 0) __result += d;
            }
            catch { }
        }

        // Helpers
        private static PartySizeLimitModel MakeVanilla()
        {
            string[] names =
            {
                "TaleWorlds.CampaignSystem.SandBox.GameComponents.Party.DefaultPartySizeLimitModel, TaleWorlds.CampaignSystem",
                "TaleWorlds.CampaignSystem.SandBox.GameComponents.DefaultPartySizeLimitModel, TaleWorlds.CampaignSystem"
            };
            for (int i = 0; i < names.Length; i++)
            {
                var t = Type.GetType(names[i], false);
                if (t != null && typeof(PartySizeLimitModel).IsAssignableFrom(t))
                {
                    try { return (PartySizeLimitModel)Activator.CreateInstance(t); }
                    catch { }
                }
            }
            // conservative shim
            return new VanillaShimPSL();
        }
    }

    internal sealed class VanillaShimPSL : PartySizeLimitModel
    {
        public override ExplainedNumber GetPartyMemberSizeLimit(PartyBase party, bool includeDescriptions)
            => new ExplainedNumber(25f, includeDescriptions);
        public override ExplainedNumber GetPartyPrisonerSizeLimit(PartyBase party, bool includeDescriptions)
            => new ExplainedNumber(25f, includeDescriptions);
        public override int GetTierPartySizeEffect(int clanTier) => 0;
        public override int GetAssumedPartySizeForLordParty(Hero hero, IFaction faction, Clan clan) => 0;
    }
}
