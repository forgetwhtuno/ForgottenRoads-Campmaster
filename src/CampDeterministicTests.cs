using System;
using System.Collections.Generic;

namespace ErenshorCampmaster
{
    // ---------------------------------------------------------------------
    // Deterministic tests for the camp lifecycle. No Unity, no game assembly,
    // no clock dependency: the tests drive an explicit UTC timeline.
    //
    // Run standalone:  tests\RUN_DETERMINISTIC_TESTS.ps1
    // Run in game:     /camp selftest
    // ---------------------------------------------------------------------
    internal static class CampDeterministicTests
    {
        internal static List<string> Run()
        {
            List<string> lines = new List<string>();
            int failures = 0;

            failures += Check(lines, "no party -> no auto camp", NoPartyNoAutoCamp());
            failures += Check(lines, "follow (no guard) -> no auto camp", GuardOffNoAutoCamp());
            failures += Check(lines, "guard only, no puller -> no auto camp", GuardWithoutPullerNoAutoCamp());
            failures += Check(lines, "partial/COOP authority -> no auto camp", PartialAuthorityNoAutoCamp());
            failures += Check(lines, "guard + puller + auto pull -> camp after stability window", FullPredicateStartsCamp());
            failures += Check(lines, "unknown player position -> no auto camp", UnknownPlayerPositionNoAutoCamp());
            failures += Check(lines, "auto pull unknown -> no auto camp", AutoPullUnknownNoAutoCamp());
            failures += Check(lines, "auto pull off -> no auto camp", AutoPullOffNoAutoCamp());
            failures += Check(lines, "predicate held briefly -> no auto camp", BriefPredicateNoAutoCamp());
            failures += Check(lines, "recognition starts only after configured 8s stability", ExactStabilityWindow());
            failures += Check(lines, "/camp here works without native camp signals", ExplicitCampWorksWithoutSignals());
            failures += Check(lines, "explicit camp requires local party, non-raid, and anchor", ExplicitCampRequiresSafeBoundary());
            failures += Check(lines, "combat does not end a camp", CombatDoesNotEndCamp());
            failures += Check(lines, "completed encounters counted after quiet period", EncountersCounted());
            failures += Check(lines, "zone change ends the camp", ZoneChangeEndsCamp());
            failures += Check(lines, "party loss ends the camp after grace", PartyLossEndsCampAfterGrace());
            failures += Check(lines, "local Sim resolution loss ends the camp after grace", LocalResolutionLossEndsCampAfterGrace());
            failures += Check(lines, "brief party churn suspends but does not end", PartyChurnSuspendsOnly());
            failures += Check(lines, "sustained departure ends the camp", DepartureEndsCamp());
            failures += Check(lines, "brief excursion does not end the camp", BriefExcursionKeepsCamp());
            failures += Check(lines, "auto camp suspends and resumes on auto pull toggle", AutoSignalLossSuspendsAndResumes());
            failures += Check(lines, "explicit camp ignores auto pull toggle", ExplicitCampIgnoresSignalLoss());
            failures += Check(lines, "unreadable native state never ends a camp", UnreadableStateDoesNotEndCamp());
            failures += Check(lines, "/camp clear ends the camp", ClearEndsCamp());
            failures += Check(lines, "raid active blocks auto camp", RaidBlocksAutoCamp());
            failures += Check(lines, "unknown roles are never guessed", UnknownRolesStayUnknown());
            failures += Check(lines, "known unassigned role remains none", KnownUnassignedRoleStaysNone());
            failures += Check(lines, "events are sequenced and replayable", EventsAreSequenced());
            failures += Check(lines, "bounded event history exposes oldest retained sequence", BoundedHistoryExposesReplayFloor());
            failures += Check(lines, "verified pull lifecycle -> Pulling and pull counter", PullLifecycleClassifiesAndCounts());
            failures += Check(lines, "combat outranks pulling activity", CombatOutranksPulling());
            failures += Check(lines, "player Puller without Sim lifecycle stays Unknown", PlayerPullerActivityStaysUnknown());
            failures += Check(lines, "native healer mana gate -> Recovering", ManaGateClassifiesRecovery());
            failures += Check(lines, "recovery event ends when mana gate clears", RecoveryEndsWhenGateClears());
            failures += Check(lines, "repeated verified pull target emits one seed", RepeatedPullTargetEmitsSeed());
            failures += Check(lines, "long verified combat emits derived rough encounter seed", RoughEncounterEmitsSeed());
            failures += Check(lines, "actual Manage Roles snapshot is exposed as verified seed", RoleSnapshotUsesNativeAssignments());

            List<string> living = CampLivingDeterministicTests.Run();
            for (int i = 0; i < living.Count; i++)
            {
                lines.Add(living[i]);
                if (living[i].IndexOf("FAIL", StringComparison.Ordinal) >= 0) failures++;
            }

            List<string> relax = RelaxDeterministicTests.Run();
            for (int i = 0; i < relax.Count; i++)
            {
                lines.Add(relax[i]);
                if (relax[i].IndexOf("FAIL", StringComparison.Ordinal) >= 0) failures++;
            }

            List<string> socialActivity = SocialActivityDeterministicTests.Run();
            for (int i = 0; i < socialActivity.Count; i++)
            {
                lines.Add(socialActivity[i]);
                if (socialActivity[i].IndexOf("FAIL", StringComparison.Ordinal) >= 0) failures++;
            }

            List<string> activityFreshness = SocialActivityFreshnessDeterministicTests.Run();
            for (int i = 0; i < activityFreshness.Count; i++)
            {
                lines.Add(activityFreshness[i]);
                if (activityFreshness[i].IndexOf("FAIL", StringComparison.Ordinal) >= 0) failures++;
            }

            lines.Add(failures == 0
                ? "Campmaster deterministic tests: ALL PASS"
                : "Campmaster deterministic tests: " + failures + " FAIL");
            return lines;
        }

        internal static int RunToConsole()
        {
            List<string> lines = Run();
            int failed = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                Console.WriteLine(lines[i]);
                if (lines[i].IndexOf("FAIL", StringComparison.Ordinal) >= 0) failed = 1;
            }
            return failed;
        }

        private static int Check(List<string> lines, string name, bool passed)
        {
            lines.Add((passed ? "PASS  " : "FAIL  ") + name);
            return passed ? 0 : 1;
        }

        // -----------------------------------------------------------------
        // Fixtures
        // -----------------------------------------------------------------
        private static readonly DateTime T0 = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);

        private static CampConfig NewConfig()
        {
            CampConfig cfg = new CampConfig();
            cfg.AutoStabilitySeconds = 8.0;
            cfg.DepartureRadius = 45f;
            cfg.DepartureGraceSeconds = 45.0;
            cfg.PartyLossGraceSeconds = 20.0;
            cfg.SignalLossGraceSeconds = 20.0;
            cfg.EncounterQuietSeconds = 8.0;
            cfg.RoughEncounterSeconds = 45.0;
            cfg.RepeatedEnemyThreshold = 3;
            return cfg;
        }

        // A fully verified camp observation: party present, everyone guarding
        // at one anchor, a named Puller, Auto Pull on, out of combat.
        private static CampObservation Camp()
        {
            CampObservation obs = new CampObservation();
            obs.ObservedUtc = T0;
            obs.ReadSucceeded = true;
            obs.Zone = "Azure";
            obs.PartyPresent = true;
            obs.PartyNames.Add("Phanty");
            obs.PartyNames.Add("Baetil");
            obs.LocalResolvedMembers = 2;
            obs.Authority = CampAuthority.FullLocal;
            obs.MainTank = RoleHolder.Sim("Baetil");
            obs.MainAssist = RoleHolder.Sim("Baetil");
            obs.Puller = RoleHolder.Sim("Phanty");
            obs.GuardActive = true;
            obs.GuardAnchor = new CampVector3(0f, 0f, 0f);
            obs.GuardAnchorTolerance = 12f;
            obs.AutoPullEnabled = true;
            obs.GroupPullModeEngaged = true;
            obs.MaxPullLevelAbove = 3;
            obs.MaxPullLevelBelow = -3;
            obs.MaxPullDistance = 500;
            obs.HoldManaFraction = 0.5f;
            obs.PullerActivelyPulling = false;
            obs.HoldingForMana = false;
            obs.InCombat = false;
            obs.RaidActive = false;
            obs.PlayerPosition = new CampVector3(0f, 0f, 0f);
            return obs;
        }

        private static CampObservation Clone(CampObservation src)
        {
            CampObservation obs = new CampObservation();
            obs.ObservedUtc = src.ObservedUtc;
            obs.ReadSucceeded = src.ReadSucceeded;
            obs.Zone = src.Zone;
            obs.PartyPresent = src.PartyPresent;
            obs.PartyNames = new List<string>(src.PartyNames);
            obs.LocalResolvedMembers = src.LocalResolvedMembers;
            obs.UnresolvedMembers = src.UnresolvedMembers;
            obs.RemoteMembers = src.RemoteMembers;
            obs.Authority = src.Authority;
            obs.MainTank = src.MainTank;
            obs.MainAssist = src.MainAssist;
            obs.DesignatedMainAssist = src.DesignatedMainAssist;
            obs.Puller = src.Puller;
            obs.CrowdControlNames = new List<string>(src.CrowdControlNames);
            obs.CrowdControlKnown = src.CrowdControlKnown;
            obs.CrowdControlPlayer = src.CrowdControlPlayer;
            obs.HealerNames = new List<string>(src.HealerNames);
            obs.HealersKnown = src.HealersKnown;
            obs.AutoPullEnabled = src.AutoPullEnabled;
            obs.GroupPullModeEngaged = src.GroupPullModeEngaged;
            obs.ForcePullTargetName = src.ForcePullTargetName;
            obs.CurrentPullTargetName = src.CurrentPullTargetName;
            obs.MaxPullLevelAbove = src.MaxPullLevelAbove;
            obs.MaxPullLevelBelow = src.MaxPullLevelBelow;
            obs.MaxPullDistance = src.MaxPullDistance;
            obs.HoldManaFraction = src.HoldManaFraction;
            obs.PullerActivelyPulling = src.PullerActivelyPulling;
            obs.HoldingForMana = src.HoldingForMana;
            obs.GuardActive = src.GuardActive;
            obs.GuardAnchor = src.GuardAnchor;
            obs.GuardAnchorSpread = src.GuardAnchorSpread;
            obs.GuardAnchorTolerance = src.GuardAnchorTolerance;
            obs.GuardedNames = new List<string>(src.GuardedNames);
            obs.PlayerPosition = src.PlayerPosition;
            obs.InCombat = src.InCombat;
            obs.RaidActive = src.RaidActive;
            return obs;
        }

        // Feeds the same observation for the given number of seconds at 1Hz.
        private static DateTime Hold(CampSessionTracker tracker, CampObservation obs, DateTime start, int seconds)
        {
            DateTime now = start;
            for (int i = 0; i < seconds; i++)
            {
                tracker.Tick(obs, now);
                now = now.AddSeconds(1);
            }
            return now;
        }

        // -----------------------------------------------------------------
        // Cases
        // -----------------------------------------------------------------
        private static bool NoPartyNoAutoCamp()
        {
            CampObservation obs = Camp();
            obs.PartyPresent = false;
            obs.PartyNames.Clear();
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            Hold(t, obs, T0, 30);
            return !t.IsActive;
        }

        private static bool GuardOffNoAutoCamp()
        {
            CampObservation obs = Camp();
            obs.GuardActive = false;
            obs.GuardAnchor = null;
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            Hold(t, obs, T0, 30);
            return !t.IsActive;
        }

        private static bool GuardWithoutPullerNoAutoCamp()
        {
            CampObservation obs = Camp();
            obs.Puller = RoleHolder.Unknown;
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            Hold(t, obs, T0, 30);
            return !t.IsActive;
        }

        private static bool PartialAuthorityNoAutoCamp()
        {
            CampObservation obs = Camp();
            obs.Authority = CampAuthority.PartialCoop;
            obs.RemoteMembers = 1;
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            Hold(t, obs, T0, 30);
            return !t.IsActive;
        }

        private static bool FullPredicateStartsCamp()
        {
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            Hold(t, Camp(), T0, 30);
            CampSnapshot snap = t.BuildSnapshot(T0.AddSeconds(30));
            return t.State == CampSessionState.Active &&
                   snap.Source == CampRecognitionSource.Auto &&
                   snap.Zone == "Azure" &&
                   snap.Anchor.HasValue &&
                   snap.AnchorOrigin == "native guard";
        }

        private static bool UnknownPlayerPositionNoAutoCamp()
        {
            CampObservation obs = Camp();
            obs.PlayerPosition = null;
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            Hold(t, obs, T0, 30);
            return !t.IsActive;
        }

        private static bool AutoPullUnknownNoAutoCamp()
        {
            CampObservation obs = Camp();
            obs.AutoPullEnabled = null;
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            Hold(t, obs, T0, 30);
            return !t.IsActive;
        }

        private static bool AutoPullOffNoAutoCamp()
        {
            CampObservation obs = Camp();
            obs.AutoPullEnabled = false;
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            Hold(t, obs, T0, 30);
            return !t.IsActive;
        }

        private static bool BriefPredicateNoAutoCamp()
        {
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            DateTime now = Hold(t, Camp(), T0, 4);           // shorter than the stability window
            CampObservation off = Camp();
            off.AutoPullEnabled = false;
            Hold(t, off, now, 5);
            return !t.IsActive;
        }

        private static bool ExactStabilityWindow()
        {
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            CampObservation obs = Camp();
            for (int i = 0; i < 8; i++) t.Tick(obs, T0.AddSeconds(i));
            if (t.IsActive) return false;
            t.Tick(obs, T0.AddSeconds(8));
            return t.IsActive;
        }

        private static bool ExplicitCampWorksWithoutSignals()
        {
            CampObservation obs = Camp();
            obs.GuardActive = false;
            obs.GuardAnchor = null;
            obs.AutoPullEnabled = false;
            obs.Puller = RoleHolder.Unknown;
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            t.RequestDeclareHere(new CampVector3(5f, 0f, 5f));
            t.Tick(obs, T0);
            CampSnapshot snap = t.BuildSnapshot(T0);
            return t.State == CampSessionState.Active &&
                   snap.Source == CampRecognitionSource.Explicit &&
                   snap.Anchor.HasValue &&
                   snap.AnchorOrigin == "player position" &&
                   !snap.Puller.IsKnown;
        }

        private static bool ExplicitCampRequiresSafeBoundary()
        {
            CampObservation noLocal = Camp();
            noLocal.LocalResolvedMembers = 0;
            noLocal.Authority = CampAuthority.PartialCoop;
            CampSessionTracker a = new CampSessionTracker(NewConfig());
            a.RequestDeclareHere(new CampVector3(0f, 0f, 0f));
            a.Tick(noLocal, T0);

            CampObservation raid = Camp();
            raid.RaidActive = true;
            CampSessionTracker b = new CampSessionTracker(NewConfig());
            b.RequestDeclareHere(new CampVector3(0f, 0f, 0f));
            b.Tick(raid, T0);

            CampSessionTracker c = new CampSessionTracker(NewConfig());
            c.RequestDeclareHere(null);
            c.Tick(Camp(), T0);

            return !a.IsActive && !b.IsActive && !c.IsActive;
        }

        private static bool CombatDoesNotEndCamp()
        {
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            DateTime now = Hold(t, Camp(), T0, 15);
            CampObservation fighting = Camp();
            fighting.InCombat = true;
            now = Hold(t, fighting, now, 60);
            CampSnapshot snap = t.BuildSnapshot(now);
            return t.State == CampSessionState.Active && snap.Activity == CampActivity.Fighting;
        }

        private static bool EncountersCounted()
        {
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            DateTime now = Hold(t, Camp(), T0, 15);
            CampObservation fighting = Camp();
            fighting.InCombat = true;

            for (int fight = 0; fight < 2; fight++)
            {
                now = Hold(t, fighting, now, 20);
                now = Hold(t, Camp(), now, 15);   // quiet period elapses
            }
            return t.BuildSnapshot(now).CompletedEncounters == 2;
        }

        private static bool ZoneChangeEndsCamp()
        {
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            DateTime now = Hold(t, Camp(), T0, 15);
            CampObservation moved = Camp();
            moved.Zone = "Krakengard";
            t.Tick(moved, now);
            return !t.IsActive;
        }

        private static bool PartyLossEndsCampAfterGrace()
        {
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            DateTime now = Hold(t, Camp(), T0, 15);
            CampObservation alone = Camp();
            alone.PartyPresent = false;
            alone.PartyNames.Clear();
            Hold(t, alone, now, 40);
            return !t.IsActive;
        }

        private static bool LocalResolutionLossEndsCampAfterGrace()
        {
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            t.RequestDeclareHere(new CampVector3(0f, 0f, 0f));
            t.Tick(Camp(), T0);

            CampObservation unresolved = Camp();
            unresolved.LocalResolvedMembers = 0;
            unresolved.UnresolvedMembers = unresolved.PartyNames.Count;
            unresolved.Authority = CampAuthority.PartialCoop;
            Hold(t, unresolved, T0.AddSeconds(1), 25);
            return !t.IsActive;
        }

        private static bool PartyChurnSuspendsOnly()
        {
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            DateTime now = Hold(t, Camp(), T0, 15);
            CampObservation alone = Camp();
            alone.PartyPresent = false;
            alone.PartyNames.Clear();
            now = Hold(t, alone, now, 5);
            if (t.State != CampSessionState.Suspended) return false;
            now = Hold(t, Camp(), now, 5);
            return t.State == CampSessionState.Active;
        }

        private static bool DepartureEndsCamp()
        {
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            DateTime now = Hold(t, Camp(), T0, 15);
            CampObservation away = Camp();
            away.PlayerPosition = new CampVector3(300f, 0f, 0f);
            away.GuardActive = true;   // Sims are still guarding at the camp
            Hold(t, away, now, 60);
            return !t.IsActive;
        }

        private static bool BriefExcursionKeepsCamp()
        {
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            DateTime now = Hold(t, Camp(), T0, 15);
            CampObservation away = Camp();
            away.PlayerPosition = new CampVector3(300f, 0f, 0f);
            now = Hold(t, away, now, 20);      // shorter than DepartureGraceSeconds
            if (!t.IsActive) return false;
            now = Hold(t, Camp(), now, 5);
            return t.State == CampSessionState.Active;
        }

        private static bool AutoSignalLossSuspendsAndResumes()
        {
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            DateTime now = Hold(t, Camp(), T0, 15);
            CampObservation pullOff = Camp();
            pullOff.AutoPullEnabled = false;
            now = Hold(t, pullOff, now, 5);
            if (t.State != CampSessionState.Suspended) return false;
            if (t.BuildSnapshot(now).Activity != CampActivity.Unknown) return false;
            now = Hold(t, Camp(), now, 3);
            return t.State == CampSessionState.Active;
        }

        private static bool ExplicitCampIgnoresSignalLoss()
        {
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            t.RequestDeclareHere(new CampVector3(0f, 0f, 0f));
            t.Tick(Camp(), T0);
            CampObservation pullOff = Camp();
            pullOff.AutoPullEnabled = false;
            pullOff.GuardActive = false;
            Hold(t, pullOff, T0.AddSeconds(1), 30);
            return t.State == CampSessionState.Active;
        }

        private static bool UnreadableStateDoesNotEndCamp()
        {
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            DateTime now = Hold(t, Camp(), T0, 15);
            CampObservation broken = new CampObservation();
            broken.ReadSucceeded = false;
            now = Hold(t, broken, now, 120);
            if (!t.IsActive) return false;
            Hold(t, Camp(), now, 3);
            return t.State == CampSessionState.Active;
        }

        private static bool ClearEndsCamp()
        {
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            DateTime now = Hold(t, Camp(), T0, 15);
            t.RequestClear();
            t.Tick(Camp(), now);
            return !t.IsActive && t.BuildSnapshot(now).State == CampSessionState.Inactive;
        }

        private static bool RaidBlocksAutoCamp()
        {
            CampObservation obs = Camp();
            obs.RaidActive = true;
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            Hold(t, obs, T0, 30);
            return !t.IsActive;
        }

        private static bool UnknownRolesStayUnknown()
        {
            CampObservation obs = Camp();
            obs.MainTank = RoleHolder.Unknown;
            obs.HealersKnown = false;
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            DateTime now = Hold(t, obs, T0, 15);
            CampSnapshot snap = t.BuildSnapshot(now);
            return !snap.MainTank.IsKnown &&
                   snap.MainTank.Describe() == "unknown" &&
                   !snap.HealersKnown;
        }

        private static bool KnownUnassignedRoleStaysNone()
        {
            CampObservation obs = Camp();
            obs.Puller = RoleHolder.None;
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            t.RequestDeclareHere(new CampVector3(0f, 0f, 0f));
            t.Tick(obs, T0);
            CampSnapshot snap = t.BuildSnapshot(T0);
            return snap.Puller.Kind == RoleHolderKind.None && snap.Puller.Describe() == "none" &&
                   snap.Puller.IsResolved && !snap.Puller.IsKnown;
        }

        private static bool EventsAreSequenced()
        {
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            DateTime now = Hold(t, Camp(), T0, 15);
            List<CampEvent> all = t.GetEventsAfter(0);
            CampEvent started = FindEvent(all, CampEventType.CampStarted);
            if (started == null) return false;
            long beforeClear = t.LatestSequence;

            t.RequestClear();
            t.Tick(Camp(), now);
            List<CampEvent> after = t.GetEventsAfter(beforeClear);
            CampEvent ended = FindEvent(after, CampEventType.CampEnded);
            if (ended == null || ended.Sequence <= beforeClear) return false;

            List<CampEvent> replay = t.GetEventsAfter(0);
            long prior = 0;
            for (int i = 0; i < replay.Count; i++)
            {
                if (replay[i].Sequence <= prior) return false;
                prior = replay[i].Sequence;
            }
            return true;
        }

        private static bool BoundedHistoryExposesReplayFloor()
        {
            CampConfig cfg = NewConfig();
            cfg.MaxRetainedEvents = 3;
            CampSessionTracker t = new CampSessionTracker(cfg);
            t.RequestDeclareHere(new CampVector3(0f, 0f, 0f));
            t.Tick(Camp(), T0);
            t.RequestClear();
            t.Tick(Camp(), T0.AddSeconds(1));
            t.RequestDeclareHere(new CampVector3(0f, 0f, 0f));
            t.Tick(Camp(), T0.AddSeconds(2));
            List<CampEvent> retained = t.GetEventsAfter(0);
            return retained.Count == 3 && t.OldestRetainedSequence == retained[0].Sequence &&
                   t.OldestRetainedSequence > 1 && t.LatestSequence == retained[retained.Count - 1].Sequence;
        }

        private static bool PullLifecycleClassifiesAndCounts()
        {
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            DateTime now = Hold(t, Camp(), T0, 15);
            CampObservation pulling = Camp();
            pulling.PullerActivelyPulling = true;
            pulling.CurrentPullTargetName = "Lost Goblin";
            t.Tick(pulling, now);
            CampSnapshot snap = t.BuildSnapshot(now);
            return snap.Activity == CampActivity.Pulling &&
                   snap.VerifiedPulls == 1 &&
                   string.Equals(snap.CurrentPullTargetName, "Lost Goblin", StringComparison.Ordinal) &&
                   FindEvent(t.GetEventsAfter(0), CampEventType.CampPullStarted) != null;
        }

        private static bool CombatOutranksPulling()
        {
            CampObservation obs = Camp();
            obs.InCombat = true;
            obs.PullerActivelyPulling = true;
            obs.HoldingForMana = true;
            return CampSessionTracker.ClassifyActivity(obs) == CampActivity.Fighting;
        }

        private static bool PlayerPullerActivityStaysUnknown()
        {
            CampObservation obs = Camp();
            obs.Puller = RoleHolder.Player("Tester");
            obs.PullerActivelyPulling = null;
            obs.InCombat = false;
            obs.HoldingForMana = false;
            return CampSessionTracker.ClassifyActivity(obs) == CampActivity.Unknown;
        }

        private static bool ManaGateClassifiesRecovery()
        {
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            DateTime now = Hold(t, Camp(), T0, 15);
            CampObservation recovery = Camp();
            recovery.PullerActivelyPulling = false;
            recovery.HoldingForMana = true;
            t.Tick(recovery, now);
            CampSnapshot snap = t.BuildSnapshot(now);
            CampEvent evt = FindEvent(t.GetEventsAfter(0), CampEventType.CampRecoveryStarted);
            return snap.Activity == CampActivity.Recovering &&
                   snap.HoldingForMana == true &&
                   evt != null && evt.VerifiedFields.ContainsKey("holdManaPercent");
        }

        private static bool RecoveryEndsWhenGateClears()
        {
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            DateTime now = Hold(t, Camp(), T0, 15);
            CampObservation recovery = Camp();
            recovery.HoldingForMana = true;
            t.Tick(recovery, now);
            now = now.AddSeconds(1);
            CampObservation clear = Camp();
            clear.HoldingForMana = false;
            t.Tick(clear, now);
            return FindEvent(t.GetEventsAfter(0), CampEventType.CampRecoveryEnded) != null &&
                   t.BuildSnapshot(now).Activity == CampActivity.Waiting;
        }

        private static bool RepeatedPullTargetEmitsSeed()
        {
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            DateTime now = Hold(t, Camp(), T0, 15);
            for (int i = 0; i < 3; i++)
            {
                CampObservation pulling = Camp();
                pulling.PullerActivelyPulling = true;
                pulling.CurrentPullTargetName = "Lost Goblin";
                t.Tick(pulling, now);
                now = now.AddSeconds(1);
                CampObservation idle = Camp();
                idle.PullerActivelyPulling = false;
                t.Tick(idle, now);
                now = now.AddSeconds(1);
            }
            CampSnapshot snap = t.BuildSnapshot(now);
            List<CampEvent> events = t.GetEventsAfter(0);
            int repeated = CountEvents(events, CampEventType.CampRepeatedEnemy);
            CampEvent evt = FindEvent(events, CampEventType.CampRepeatedEnemy);
            return snap.VerifiedPulls == 3 && snap.RepeatedEnemySignals == 1 && repeated == 1 &&
                   evt != null && evt.VerifiedFields.ContainsKey("target") &&
                   evt.VerifiedFields["target"] == "Lost Goblin";
        }

        private static bool RoughEncounterEmitsSeed()
        {
            CampConfig cfg = NewConfig();
            cfg.RoughEncounterSeconds = 10.0;
            CampSessionTracker t = new CampSessionTracker(cfg);
            DateTime now = Hold(t, Camp(), T0, 15);
            CampObservation fighting = Camp();
            fighting.InCombat = true;
            now = Hold(t, fighting, now, 20);
            now = Hold(t, Camp(), now, 10);
            CampSnapshot snap = t.BuildSnapshot(now);
            CampEvent rough = FindEvent(t.GetEventsAfter(0), CampEventType.CampRoughEncounter);
            return snap.CompletedEncounters == 1 && snap.RoughEncounters == 1 && rough != null &&
                   rough.VerifiedFields.ContainsKey("classification") &&
                   rough.VerifiedFields["classification"] == "CampmasterDerivedLongCombat";
        }

        private static bool RoleSnapshotUsesNativeAssignments()
        {
            CampSessionTracker t = new CampSessionTracker(NewConfig());
            DateTime now = Hold(t, Camp(), T0, 15);
            CampEvent first = FindEvent(t.GetEventsAfter(0), CampEventType.CampRoleSnapshot);
            if (first == null || !first.VerifiedFields.ContainsKey("mainTank") ||
                first.VerifiedFields["mainTank"] != "Baetil" ||
                !first.VerifiedFields.ContainsKey("puller") || first.VerifiedFields["puller"] != "Phanty")
                return false;

            long before = t.LatestSequence;
            CampObservation changed = Camp();
            changed.Puller = RoleHolder.Sim("Baetil");
            t.Tick(changed, now);
            CampEvent second = FindEvent(t.GetEventsAfter(before), CampEventType.CampRoleSnapshot);
            return second != null && second.VerifiedFields.ContainsKey("puller") &&
                   second.VerifiedFields["puller"] == "Baetil";
        }

        private static CampEvent FindEvent(List<CampEvent> events, CampEventType type)
        {
            if (events == null) return null;
            for (int i = 0; i < events.Count; i++) if (events[i].Type == type) return events[i];
            return null;
        }

        private static int CountEvents(List<CampEvent> events, CampEventType type)
        {
            int count = 0;
            if (events == null) return count;
            for (int i = 0; i < events.Count; i++) if (events[i].Type == type) count++;
            return count;
        }
    }
}
