using System;
using System.Collections.Generic;

namespace ErenshorCampmaster
{
    internal static class CampLivingDeterministicTests
    {
        internal static List<string> Run()
        {
            List<string> lines = new List<string>();
            Add(lines, "living camp start assigns every admitted local participant", StartAssignsParticipants());
            Add(lines, "living camp selection is deterministic", SelectionIsDeterministic());
            Add(lines, "living camp excludes remote/dead/unresolved participants", InvalidParticipantsExcluded());
            Add(lines, "combat immediately interrupts all camp ownership", CombatInterrupts());
            Add(lines, "native Hunt Camp pull interrupts contextual camp ownership", NativePullInterrupts());
            Add(lines, "camp activities resume cleanly after combat", CombatResume());
            Add(lines, "participant removal releases activity ownership", ParticipantRemoval());
            Add(lines, "cancel/plugin stop clears activity ownership", StopClears());
            Add(lines, "session/scene replacement cannot leave stale activities", SessionReplacementClears());
            Add(lines, "scene/camp end clears runtime ownership", SceneEndClears());
            Add(lines, "repeated camp sessions start from clean runtime state", RepeatedSessions());
            Add(lines, "living event ids remain session-scoped", EventIdsAreSessionScoped());
            Add(lines, "missing optional Journal fails closed", OptionalJournalAbsentSafe());
            Add(lines, "living event history stays bounded", EventHistoryBounded());
            Add(lines, "unsupported disagreement subject stays generic", CampLivingActivityTracker.BuildMinorDisagreement("Fiora", 0, 14) == "Fiora had a minor disagreement with the group.");
            Add(lines, "current preparation may ground disagreement subject", CampLivingActivityTracker.BuildMinorDisagreement("Fiora", 4, 14).IndexOf("preparations were sufficient", StringComparison.Ordinal) >= 0);
            Add(lines, "disagreement never fabricates route destination or quest", NoInventedDisagreementSubject());
            Add(lines, "watch uses informational presentation", CampLivingActivityTracker.PresentationFor(CampLivingEventType.WatchEvent) == CampPresentationCategory.Informational);
            Add(lines, "disagreement uses warning presentation", CampLivingActivityTracker.PresentationFor(CampLivingEventType.MinorDisagreement) == CampPresentationCategory.Warning);
            Add(lines, "preparation uses neutral presentation", CampLivingActivityTracker.PresentationFor(CampLivingEventType.PreparationChanged) == CampPresentationCategory.Neutral);
            return lines;
        }

        private static bool NoInventedDisagreementSubject()
        {
            string generic = CampLivingActivityTracker.BuildMinorDisagreement("Fiora", 0, 14);
            string grounded = CampLivingActivityTracker.BuildMinorDisagreement("Fiora", 4, 14);
            string combined = (generic + " " + grounded).ToLowerInvariant();
            return combined.IndexOf("route", StringComparison.Ordinal) < 0 && combined.IndexOf("destination", StringComparison.Ordinal) < 0 &&
                combined.IndexOf("quest", StringComparison.Ordinal) < 0 && combined.IndexOf("loot", StringComparison.Ordinal) < 0;
        }

        private static void Add(List<string> lines, string name, bool passed)
        {
            lines.Add((passed ? "PASS  " : "FAIL  ") + name);
        }

        private static bool StartAssignsParticipants()
        {
            DateTime now = Utc(0);
            CampLivingActivityTracker tracker = new CampLivingActivityTracker();
            CampObservation obs = Observation(false, Participant(1, "A"), Participant(2, "B"), Participant(3, "C"));
            tracker.Tick(CampLivingMode.Relax, "r1", true, obs, now);
            CampLivingSnapshot snap = tracker.BuildSnapshot();
            if (snap.Activities.Count != 3) return false;
            int watch = 0;
            for (int i = 0; i < snap.Activities.Count; i++) if (snap.Activities[i].Type == CampLivingActivityType.Watch) watch++;
            return watch == 1;
        }

        private static bool SelectionIsDeterministic()
        {
            DateTime now = Utc(0);
            CampObservation obs = Observation(false, Participant(10, "A"), Participant(20, "B"));
            CampLivingActivityTracker a = new CampLivingActivityTracker();
            CampLivingActivityTracker b = new CampLivingActivityTracker();
            a.Tick(CampLivingMode.Relax, "same", true, obs, now);
            b.Tick(CampLivingMode.Relax, "same", true, obs, now);
            CampLivingSnapshot sa = a.BuildSnapshot(); CampLivingSnapshot sb = b.BuildSnapshot();
            if (sa.Activities.Count != sb.Activities.Count) return false;
            for (int i = 0; i < sa.Activities.Count; i++)
                if (sa.Activities[i].StableId != sb.Activities[i].StableId || sa.Activities[i].Type != sb.Activities[i].Type || sa.Activities[i].EndsUtc != sb.Activities[i].EndsUtc) return false;
            return true;
        }

        private static bool InvalidParticipantsExcluded()
        {
            CampParticipantObservation remote = Participant(1, "Remote"); remote.Remote = true; remote.LocalUsable = false;
            CampParticipantObservation dead = Participant(2, "Dead"); dead.KnownDead = true; dead.LocalUsable = false;
            CampParticipantObservation unresolved = Participant(3, "Loading"); unresolved.LocalUsable = false;
            CampParticipantObservation local = Participant(4, "Local");
            CampLivingActivityTracker tracker = new CampLivingActivityTracker();
            tracker.Tick(CampLivingMode.Relax, "r", true, Observation(false, remote, dead, unresolved, local), Utc(0));
            CampLivingSnapshot snap = tracker.BuildSnapshot();
            return snap.Activities.Count == 1 && snap.Activities[0].StableId == 4;
        }

        private static bool CombatInterrupts()
        {
            CampLivingActivityTracker tracker = new CampLivingActivityTracker();
            CampObservation calm = Observation(false, Participant(1, "A"), Participant(2, "B"));
            tracker.Tick(CampLivingMode.Relax, "r", true, calm, Utc(0));
            if (tracker.ActiveActivityCount != 2) return false;
            tracker.Tick(CampLivingMode.Relax, "r", true, Observation(true, Participant(1, "A"), Participant(2, "B")), Utc(1));
            CampLivingSnapshot snap = tracker.BuildSnapshot();
            return tracker.ActiveActivityCount == 0 && snap.SuspendedForCombat && HasEvent(snap, CampLivingEventType.CombatSuspended);
        }

        private static bool NativePullInterrupts()
        {
            CampLivingActivityTracker tracker = new CampLivingActivityTracker();
            CampObservation obs = Observation(false, Participant(1, "A"), Participant(2, "B"));
            tracker.Tick(CampLivingMode.HuntCamp, "h", true, obs, Utc(0));
            if (tracker.ActiveActivityCount != 2) return false;
            obs.PullerActivelyPulling = true;
            tracker.Tick(CampLivingMode.HuntCamp, "h", true, obs, Utc(1));
            return tracker.ActiveActivityCount == 0;
        }

        private static bool CombatResume()
        {
            CampLivingActivityTracker tracker = new CampLivingActivityTracker();
            CampObservation calm = Observation(false, Participant(1, "A"));
            tracker.Tick(CampLivingMode.Relax, "r", true, calm, Utc(0));
            tracker.Tick(CampLivingMode.Relax, "r", true, Observation(true, Participant(1, "A")), Utc(1));
            tracker.Tick(CampLivingMode.Relax, "r", true, calm, Utc(2));
            CampLivingSnapshot snap = tracker.BuildSnapshot();
            return !snap.SuspendedForCombat && snap.Activities.Count == 1 && HasEvent(snap, CampLivingEventType.CombatResumed);
        }

        private static bool ParticipantRemoval()
        {
            CampLivingActivityTracker tracker = new CampLivingActivityTracker();
            tracker.Tick(CampLivingMode.Relax, "r", true, Observation(false, Participant(1, "A"), Participant(2, "B")), Utc(0));
            tracker.Tick(CampLivingMode.Relax, "r", true, Observation(false, Participant(1, "A")), Utc(1));
            CampLivingSnapshot snap = tracker.BuildSnapshot();
            return snap.Activities.Count == 1 && snap.Activities[0].StableId == 1 && HasEvent(snap, CampLivingEventType.ActivityInterrupted);
        }

        private static bool StopClears()
        {
            CampLivingActivityTracker tracker = new CampLivingActivityTracker();
            tracker.Tick(CampLivingMode.Relax, "r", true, Observation(false, Participant(1, "A")), Utc(0));
            tracker.Stop(Utc(1), "plugin disabled");
            CampLivingSnapshot snap = tracker.BuildSnapshot();
            return !tracker.IsActive && tracker.ActiveActivityCount == 0 && snap.Mode == CampLivingMode.None;
        }

        private static bool SessionReplacementClears()
        {
            CampLivingActivityTracker tracker = new CampLivingActivityTracker();
            tracker.Tick(CampLivingMode.Relax, "zoneA", true, Observation(false, Participant(1, "A")), Utc(0));
            tracker.Tick(CampLivingMode.Relax, "zoneB", true, Observation(false, Participant(2, "B")), Utc(1));
            CampLivingSnapshot snap = tracker.BuildSnapshot();
            return snap.SessionId == "zoneB" && snap.Activities.Count == 1 && snap.Activities[0].StableId == 2;
        }

        private static bool SceneEndClears()
        {
            CampLivingActivityTracker tracker = new CampLivingActivityTracker();
            CampObservation obs = Observation(false, Participant(1, "A"));
            tracker.Tick(CampLivingMode.Relax, "zoneA", true, obs, Utc(0));
            tracker.Tick(CampLivingMode.None, string.Empty, false, obs, Utc(1));
            CampLivingSnapshot snap = tracker.BuildSnapshot();
            return !tracker.IsActive && snap.Mode == CampLivingMode.None && snap.Activities.Count == 0;
        }

        private static bool RepeatedSessions()
        {
            CampLivingActivityTracker tracker = new CampLivingActivityTracker();
            CampObservation obs = Observation(false, Participant(1, "A"));
            tracker.Tick(CampLivingMode.Relax, "one", true, obs, Utc(0));
            tracker.Stop(Utc(1), "cancel");
            tracker.Tick(CampLivingMode.HuntCamp, "two", true, obs, Utc(2));
            CampLivingSnapshot snap = tracker.BuildSnapshot();
            return snap.Mode == CampLivingMode.HuntCamp && snap.Preparation == 0 && snap.Activities.Count == 1;
        }

        private static bool EventIdsAreSessionScoped()
        {
            CampObservation obs = Observation(false, Participant(1, "A"));
            CampLivingActivityTracker a = new CampLivingActivityTracker();
            CampLivingActivityTracker b = new CampLivingActivityTracker();
            a.Tick(CampLivingMode.Relax, "relax-one", true, obs, Utc(0));
            b.Tick(CampLivingMode.Relax, "relax-two", true, obs, Utc(0));
            List<CampLivingEvent> ea = a.GetEventsAfter(0);
            List<CampLivingEvent> eb = b.GetEventsAfter(0);
            return ea.Count > 0 && eb.Count > 0 && !string.Equals(ea[0].EventId, eb[0].EventId, StringComparison.Ordinal);
        }

        private static bool OptionalJournalAbsentSafe()
        {
            CampLivingEvent evt = new CampLivingEvent();
            evt.EventId = "test-camp-journal-absent"; evt.Detail = "Watch test."; evt.Meaningful = true; evt.Type = CampLivingEventType.WatchEvent;
            return !OptionalJournalBridge.TryPost(evt);
        }

        private static bool EventHistoryBounded()
        {
            CampLivingActivityTracker tracker = new CampLivingActivityTracker();
            CampObservation obs = Observation(false, Participant(1, "A"));
            DateTime now = Utc(0);
            tracker.Tick(CampLivingMode.Relax, "r", true, obs, now);
            for (int i = 1; i <= 160; i++) tracker.Tick(CampLivingMode.Relax, "r", true, obs, now.AddSeconds(i * 70));
            return tracker.GetEventsAfter(0).Count <= CampLivingActivityTracker.MaxEvents && tracker.ActiveActivityCount <= 1;
        }

        private static bool HasEvent(CampLivingSnapshot snap, CampLivingEventType type)
        {
            for (int i = 0; i < snap.RecentEvents.Count; i++) if (snap.RecentEvents[i].Type == type) return true;
            return false;
        }

        private static CampObservation Observation(bool combat, params CampParticipantObservation[] participants)
        {
            CampObservation obs = new CampObservation();
            obs.ReadSucceeded = true; obs.Zone = "Test"; obs.InCombat = combat; obs.PartyPresent = participants.Length > 0;
            for (int i = 0; i < participants.Length; i++)
            {
                CampParticipantObservation p = participants[i]; obs.Participants.Add(p); obs.PartyNames.Add(p.Name);
                if (p.LocalUsable && !p.Remote && !p.KnownDead) obs.LocalResolvedMembers++;
                else obs.UnresolvedMembers++;
            }
            obs.Authority = CampAuthority.FullLocal;
            return obs;
        }

        private static CampParticipantObservation Participant(int id, string name)
        {
            CampParticipantObservation value = new CampParticipantObservation();
            value.StableId = id; value.Name = name; value.LocalUsable = true; return value;
        }

        private static DateTime Utc(int seconds)
        {
            return new DateTime(638900000000000000L, DateTimeKind.Utc).AddSeconds(seconds);
        }
    }
}
