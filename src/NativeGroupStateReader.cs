using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ErenshorCampmaster
{
    // ---------------------------------------------------------------------
    // Reads verified native Erenshor state into a CampObservation.
    //
    // Every member touched here was located in the installed
    // Assembly-CSharp.dll and its meaning traced through its own read/write
    // call graph. See docs/CAMP_PHASE1_ASSEMBLY_FINDINGS.md.
    //
    // This reader NEVER writes native state.
    // ---------------------------------------------------------------------
    internal static class NativeGroupStateReader
    {
        internal static CampObservation Read(DateTime nowUtc)
        {
            CampObservation obs = new CampObservation();
            obs.ObservedUtc = nowUtc;

            try
            {
                obs.Zone = SafeSceneName();
                obs.PlayerPosition = SafePlayerPosition();
                obs.InCombat = SafeBool(delegate { return GameData.InCombat; });
                obs.RaidActive = SafeBool(delegate { return GameData.RaidActive; });

                SimPlayerGrouping grouping = null;
                try { grouping = GameData.SimPlayerGrouping; }
                catch { }

                ReadParty(obs);
                ReadRoles(obs, grouping);
                ReadPullState(obs, grouping);
                ReadHoldingForMana(obs, grouping);
                ReadGuardAnchor(obs, grouping);
                // Only the native pull lifecycle is current activity evidence. ForcePullTarget is
                // retained storage: current Assembly-CSharp sets it in IndivPull and does not clear
                // it when that one-shot pull completes. A populated selected/forced/old target is
                // therefore context, never a level-triggered "the player is still active" signal.
                obs.MeaningfulGameplayActivity = NativeMeaningfulActivityPolicy.Resolve(obs.PullerActivelyPulling,
                    obs.CurrentPullTargetName, obs.ForcePullTargetName, out obs.MeaningfulGameplayReason);

                obs.ReadSucceeded = true;
            }
            catch (Exception)
            {
                // Fail closed: an incomplete read is reported as unreadable
                // rather than as a set of confident "false" facts.
                obs.ReadSucceeded = false;
            }

            return obs;
        }

        // -----------------------------------------------------------------
        // Party (GameData.GroupMembers is the authoritative roster; it holds
        // Sim trackings only, the local player is implicit).
        // -----------------------------------------------------------------
        private static void ReadParty(CampObservation obs)
        {
            SimPlayerTracking[] members = null;
            try { members = GameData.GroupMembers; }
            catch { }
            if (members == null) return;

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < members.Length; i++)
            {
                SimPlayerTracking tracking = members[i];
                if (tracking == null) continue;
                string name = SafeTrackingName(tracking);
                if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;

                obs.PartyNames.Add(name);
                obs.PartyPresent = true;

                CampParticipantObservation participant = new CampParticipantObservation();
                participant.Name = name;
                participant.StableId = SafeTrackingId(tracking);
                obs.Participants.Add(participant);

                SimPlayer sim = SafeAvatar(tracking);
                if (sim == null) { obs.UnresolvedMembers++; continue; }
                participant.KnownDead = IsKnownDead(sim);
                if (CoopCompatibility.IsRemoteCoopHuman(sim) || CoopCompatibility.IsRemoteCoopSim(sim))
                {
                    participant.Remote = true;
                    obs.RemoteMembers++;
                    continue;
                }
                if (!IsUsableLocalPartySim(sim)) { obs.UnresolvedMembers++; continue; }
                participant.LocalUsable = true;
                obs.LocalResolvedMembers++;
            }

            if (!obs.PartyPresent) obs.Authority = CampAuthority.Unknown;
            else if (obs.RemoteMembers > 0 || obs.UnresolvedMembers > 0) obs.Authority = CampAuthority.PartialCoop;
            else obs.Authority = CampAuthority.FullLocal;
        }

        // -----------------------------------------------------------------
        // Native Role Manager state.
        //
        // GroupTasks (the Manage Roles UI) writes these fields on
        // GameData.SimPlayerGrouping; the Sim AI reads them. A null role
        // reference paired with the matching PlayerIsX flag means the player
        // holds the role.
        // -----------------------------------------------------------------
        private static void ReadRoles(CampObservation obs, SimPlayerGrouping grouping)
        {
            if (grouping == null) return;
            string playerName = SafePlayerName();

            SimPlayerTracking mainTankTracking;
            bool mainTankRead = TryTracking(delegate { return grouping.MainTank; }, out mainTankTracking);
            obs.MainTank = ResolveRole(mainTankTracking, mainTankRead,
                                       SafeBool(delegate { return grouping.PlayerIsTank; }), playerName);

            SimPlayerTracking mainAssistTracking;
            bool mainAssistRead = TryTracking(delegate { return grouping.MainAssist; }, out mainAssistTracking);
            obs.MainAssist = ResolveRole(mainAssistTracking, mainAssistRead,
                                         SafeBool(delegate { return grouping.PlayerIsMA; }), playerName);

            SimPlayerTracking designatedTracking;
            bool designatedRead = TryTracking(delegate { return grouping.DesignatedMA; }, out designatedTracking);
            obs.DesignatedMainAssist = ResolveRole(designatedTracking, designatedRead,
                                                   SafeBool(delegate { return grouping.PlayerIsDesignatedMA; }), playerName);

            SimPlayerTracking pullerTracking;
            bool pullerRead = TryTracking(delegate { return grouping.Puller; }, out pullerTracking);
            bool? playerIsPuller = SafeBool(delegate { return grouping.PlayerIsPuller; });
            obs.Puller = ResolveRole(pullerTracking, pullerRead, playerIsPuller, playerName);
            ReadPullerActivity(obs, pullerTracking);

            try
            {
                List<SimPlayerTracking> cc = grouping.CC;
                if (cc != null)
                {
                    for (int i = 0; i < cc.Count; i++)
                    {
                        string name = SafeTrackingName(cc[i]);
                        if (!string.IsNullOrEmpty(name)) obs.CrowdControlNames.Add(name);
                    }
                    obs.CrowdControlKnown = true;
                }
                bool? playerCC = SafeBool(delegate { return grouping.PlayerIsCC; });
                if (playerCC == true)
                {
                    obs.CrowdControlPlayer = true;
                    obs.CrowdControlNames.Add(string.IsNullOrEmpty(playerName) ? "you" : playerName + " (you)");
                }
            }
            catch { obs.CrowdControlKnown = false; }

            try
            {
                List<SimPlayerTracking> heals = grouping.Heals;
                if (heals != null)
                {
                    for (int i = 0; i < heals.Count; i++)
                    {
                        string name = SafeTrackingName(heals[i]);
                        if (!string.IsNullOrEmpty(name)) obs.HealerNames.Add(name);
                    }
                    obs.HealersKnown = true;
                }
            }
            catch { obs.HealersKnown = false; }
        }

        private static RoleHolder ResolveRole(SimPlayerTracking tracking, bool trackingReadSucceeded, bool? playerHolds, string playerName)
        {
            if (!trackingReadSucceeded || !playerHolds.HasValue) return RoleHolder.Unknown;
            string name = SafeTrackingName(tracking);
            if (!string.IsNullOrEmpty(name)) return RoleHolder.Sim(name);
            if (playerHolds.Value) return RoleHolder.Player(playerName);
            return RoleHolder.None;
        }

        // -----------------------------------------------------------------
        // Phase-3 pull lifecycle. The installed assembly findings verified
        // SimPlayer.CurrentPullPhase (NotPulling=0 .. AttackTarget=5) and
        // SimPlayer.PullTarget. They are read through cached reflection here
        // so a visibility/signature drift fails closed instead of breaking the
        // whole plugin build against a neighboring Erenshor version.
        // -----------------------------------------------------------------
        private static readonly FieldInfo SimIndexField =
            typeof(SimPlayerTracking).GetField("simIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo CurrentPullPhaseField =
            typeof(SimPlayer).GetField("CurrentPullPhase", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo PullTargetField =
            typeof(SimPlayer).GetField("PullTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static void ReadPullerActivity(CampObservation obs, SimPlayerTracking pullerTracking)
        {
            if (obs == null || pullerTracking == null) return;
            SimPlayer sim = SafeAvatar(pullerTracking);
            if (sim == null || CoopCompatibility.IsRemoteCoopHuman(sim) ||
                CoopCompatibility.IsRemoteCoopSim(sim) || !IsUsableLocalPartySim(sim)) return;

            if (CurrentPullPhaseField != null)
            {
                try
                {
                    object phase = CurrentPullPhaseField.GetValue(sim);
                    if (phase != null)
                    {
                        // Enum value 0 is verified as NotPulling. Avoid taking a
                        // compile-time dependency on the nested enum's visibility.
                        obs.PullerActivelyPulling = Convert.ToInt32(phase) != 0;
                    }
                }
                catch { obs.PullerActivelyPulling = null; }
            }

            // PullTarget may remain populated outside an active lifecycle.
            // Only expose it as CURRENT while the native pull phase itself is
            // positively verified active, otherwise omit it rather than risk
            // turning stale storage into a present-tense fact.
            if (obs.PullerActivelyPulling == true && PullTargetField != null)
            {
                try
                {
                    Character target = PullTargetField.GetValue(sim) as Character;
                    if (target != null && target.MyStats != null && !string.IsNullOrEmpty(target.MyStats.MyName))
                        obs.CurrentPullTargetName = target.MyStats.MyName.Trim();
                }
                catch { }
            }
        }

        // -----------------------------------------------------------------
        // Native pull state.
        //
        // PullConstant is the Auto Pull toggle (Shift+5 / the party window's
        // "Auto Pull: ON/OFF" button).
        //
        // isPulling is NOT the Auto Pull toggle: it is set true at the end of
        // every GroupPull (including a one-shot manual Pull Target, which
        // explicitly clears PullConstant) and false by HoldPulls / RunAway /
        // scene change. It is read only by the gamepad D-pad pull toggle. It
        // is surfaced as "group pull mode engaged" and is never used as
        // evidence that Auto Pull is on.
        // -----------------------------------------------------------------
        private static void ReadPullState(CampObservation obs, SimPlayerGrouping grouping)
        {
            if (grouping == null) return;

            obs.AutoPullEnabled = SafeBool(delegate { return grouping.PullConstant; });
            obs.GroupPullModeEngaged = SafeBool(delegate { return grouping.isPulling; });
            obs.MaxPullLevelAbove = SafeInt(delegate { return grouping.PullerRangeHigh; });
            obs.MaxPullLevelBelow = SafeInt(delegate { return grouping.PullerRangeLow; });
            obs.MaxPullDistance = SafeInt(delegate { return grouping.MaxPullDist; });
            obs.HoldManaFraction = SafeFloat(delegate { return grouping.ManaNeededForPull; });

            try
            {
                Character forced = grouping.ForcePullTarget;
                if (forced != null && forced.MyStats != null && !string.IsNullOrEmpty(forced.MyStats.MyName))
                    obs.ForcePullTargetName = forced.MyStats.MyName.Trim();
            }
            catch { }
        }

        // -----------------------------------------------------------------
        // Phase-3 native mana hold. There is no native "holding" flag; the
        // installed assembly proves CheckPullReadiness iterates Heals and
        // blocks when any healer has CurrentMana below
        // GetCurrentMaxMana()*ManaNeededForPull. Recompute that exact gate.
        //
        // True is safe as soon as one readable healer is below threshold.
        // False is only safe when every listed healer was locally readable.
        // Otherwise unknown stays null. The player is intentionally excluded
        // because native Heals is a SimPlayerTracking list.
        // -----------------------------------------------------------------
        private static void ReadHoldingForMana(CampObservation obs, SimPlayerGrouping grouping)
        {
            if (obs == null || grouping == null || !obs.HoldManaFraction.HasValue) return;

            List<SimPlayerTracking> heals;
            try { heals = grouping.Heals; }
            catch { return; }
            if (heals == null) return;
            if (heals.Count == 0)
            {
                obs.HoldingForMana = false;
                return;
            }

            bool anyReadable = false;
            bool allReadable = true;
            float threshold = obs.HoldManaFraction.Value;

            for (int i = 0; i < heals.Count; i++)
            {
                SimPlayerTracking tracking = heals[i];
                SimPlayer sim = SafeAvatar(tracking);
                if (sim == null || CoopCompatibility.IsRemoteCoopHuman(sim) ||
                    CoopCompatibility.IsRemoteCoopSim(sim) || !IsUsableLocalPartySim(sim) || sim.MyStats == null)
                {
                    allReadable = false;
                    continue;
                }

                try
                {
                    int currentMana = sim.MyStats.CurrentMana;
                    double maxMana = Convert.ToDouble(sim.MyStats.GetCurrentMaxMana());
                    anyReadable = true;
                    if (currentMana < (maxMana * threshold))
                    {
                        obs.HoldingForMana = true;
                        return;
                    }
                }
                catch
                {
                    allReadable = false;
                }
            }

            if (anyReadable && allReadable) obs.HoldingForMana = false;
        }

        // -----------------------------------------------------------------
        // Native Guard/Stay anchor.
        //
        // GroupGuard() assigns every party Sim a GuardSpot at the player's
        // position (plus that Sim's own formation offset) and sets
        // SimPlayer.GuardSpot = true. GroupFollow() clears it via
        // FreeFollow(). Combat does not clear it (that uses the separate
        // suspendGuard field), so it is a stable camp anchor.
        //
        // The tolerance below is derived from the native formation spread
        // (randomizeMagnitude = 3 + SimPlayerGrouping.SpreadMagnitude), so it
        // tracks the player's own formation setting rather than a guessed
        // constant.
        // -----------------------------------------------------------------
        private static void ReadGuardAnchor(CampObservation obs, SimPlayerGrouping grouping)
        {
            SimPlayerTracking[] members = null;
            try { members = GameData.GroupMembers; }
            catch { }
            if (members == null) return;

            float spread = 0f;
            if (grouping != null)
            {
                float? s = SafeFloat(delegate { return grouping.SpreadMagnitude; });
                if (s.HasValue) spread = s.Value;
            }
            obs.GuardAnchorTolerance = Math.Max(8f, (1.5f * (3f + spread)) + 2f);

            int considered = 0;
            int guarding = 0;
            float sx = 0f, sy = 0f, sz = 0f;
            List<Vector3> positions = new List<Vector3>();

            for (int i = 0; i < members.Length; i++)
            {
                SimPlayerTracking tracking = members[i];
                if (tracking == null) continue;
                SimPlayer sim = SafeAvatar(tracking);
                if (sim == null) continue;
                if (CoopCompatibility.IsRemoteCoopHuman(sim) || CoopCompatibility.IsRemoteCoopSim(sim)) continue;
                if (!IsUsableLocalPartySim(sim)) continue;

                considered++;
                bool isGuarding;
                try { isGuarding = sim.GuardSpot; }
                catch { continue; }
                if (!isGuarding) continue;

                Vector3 pos;
                try { pos = sim.GetGuardPos(); }
                catch { continue; }

                guarding++;
                positions.Add(pos);
                sx += pos.x; sy += pos.y; sz += pos.z;
                string name = SafeTrackingName(tracking);
                if (!string.IsNullOrEmpty(name)) obs.GuardedNames.Add(name);
            }

            if (considered == 0)
            {
                obs.GuardActive = null;   // nothing locally resolvable: unknown, not false
                return;
            }

            // Guard is only treated as verified when every resolvable local
            // party Sim is guarding. A partially-guarding party is not an
            // intentional camp.
            obs.GuardActive = (guarding == considered) && guarding > 0;
            if (obs.GuardActive != true || positions.Count == 0) return;

            Vector3 centroid = new Vector3(sx / guarding, sy / guarding, sz / guarding);
            float worst = 0f;
            for (int i = 0; i < positions.Count; i++)
            {
                float d = Vector3.Distance(positions[i], centroid);
                if (d > worst) worst = d;
            }
            obs.GuardAnchorSpread = worst;

            if (worst > obs.GuardAnchorTolerance)
            {
                // Guard flags are set but the guard spots are not one camp.
                obs.GuardActive = false;
                return;
            }

            obs.GuardAnchor = new CampVector3(centroid.x, centroid.y, centroid.z);
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------
        internal static CampVector3? SafePlayerPosition()
        {
            try
            {
                if (GameData.PlayerControl == null) return null;
                Transform t = GameData.PlayerControl.transform;
                if (t == null) return null;
                Vector3 p = t.position;
                return new CampVector3(p.x, p.y, p.z);
            }
            catch { return null; }
        }

        internal static string SafePlayerName()
        {
            try
            {
                if (GameData.PlayerStats != null && !string.IsNullOrEmpty(GameData.PlayerStats.MyName))
                    return GameData.PlayerStats.MyName.Trim();
            }
            catch { }
            return null;
        }

        internal static string SafeSceneName()
        {
            try
            {
                string scene = GameData.SceneName;
                return string.IsNullOrEmpty(scene) ? null : scene.Trim();
            }
            catch { return null; }
        }

        private static bool IsUsableLocalPartySim(SimPlayer sim)
        {
            try
            {
                if (sim == null || sim.gameObject == null || !sim.gameObject.activeInHierarchy) return false;
                if (sim.MyStats == null || sim.MyStats.Myself == null || !sim.MyStats.Myself.Alive) return false;
                if (!sim.InGroup) return false;
                return GameData.SimPlayerGrouping != null && GameData.SimPlayerGrouping.IsSimInPlayerGroup(sim);
            }
            catch { return false; }
        }

        private static bool IsKnownDead(SimPlayer sim)
        {
            try { return sim != null && sim.MyStats != null && sim.MyStats.Myself != null && !sim.MyStats.Myself.Alive; }
            catch { return false; }
        }

        private static int SafeTrackingId(SimPlayerTracking tracking)
        {
            if (tracking == null || SimIndexField == null) return -1;
            try { return Convert.ToInt32(SimIndexField.GetValue(tracking)); }
            catch { return -1; }
        }

        private static SimPlayer SafeAvatar(SimPlayerTracking tracking)
        {
            try { return tracking == null ? null : tracking.MyAvatar; }
            catch { return null; }
        }

        private static string SafeTrackingName(SimPlayerTracking tracking)
        {
            try
            {
                if (tracking == null) return null;
                return string.IsNullOrEmpty(tracking.SimName) ? null : tracking.SimName.Trim();
            }
            catch { return null; }
        }

        private static SimPlayerTracking SafeTracking(Func<SimPlayerTracking> read)
        {
            try { return read(); }
            catch { return null; }
        }

        private static bool TryTracking(Func<SimPlayerTracking> read, out SimPlayerTracking value)
        {
            try
            {
                value = read();
                return true;
            }
            catch
            {
                value = null;
                return false;
            }
        }

        private static bool? SafeBool(Func<bool> read)
        {
            try { return read(); }
            catch { return null; }
        }

        private static int? SafeInt(Func<int> read)
        {
            try { return read(); }
            catch { return null; }
        }

        private static float? SafeFloat(Func<float> read)
        {
            try { return read(); }
            catch { return null; }
        }
    }
}
