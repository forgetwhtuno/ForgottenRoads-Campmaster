using System;
using System.Collections.Generic;

namespace ErenshorCampmaster
{
    // ---------------------------------------------------------------------
    // Small, stable, READ-ONLY compatibility surface for optional consumers
    // (Deep Sims compatibility reader, Practice Duels, ...).
    //
    // Everything returned is primitive/string data copied out of Campmaster
    // state. Consumers can reach this by reflection without taking a hard
    // dependency, and cannot use it to drive gameplay.
    //
    // Contract rules:
    //  * an absent value means UNKNOWN, never "false" or "zero";
    //  * no method here ever mutates Campmaster or native Erenshor state.
    // ---------------------------------------------------------------------
    public static class CampmasterApi
    {
        public const int SchemaVersion = 3;
        public const int SocialContractVersion = 1;

        /// <summary>True only while a Hunt Camp session exists (Active or Suspended).</summary>
        public static bool IsHuntCampActive
        {
            get
            {
                CampmasterPlugin plugin = CampmasterPlugin.Instance;
                return plugin != null && plugin.Tracker != null && plugin.Tracker.IsActive;
            }
        }

        /// <summary>True while an explicit Relax session is active or suspended for combat.</summary>
        public static bool IsRelaxActive
        {
            get
            {
                CampmasterPlugin plugin = CampmasterPlugin.Instance;
                return plugin != null && plugin.RelaxTracker != null && plugin.RelaxTracker.IsActive;
            }
        }

        /// <summary>True only when the current Relax context was recognized automatically.</summary>
        public static bool IsAutoRelaxActive
        {
            get
            {
                CampmasterPlugin plugin = CampmasterPlugin.Instance;
                return plugin != null && plugin.RelaxTracker != null && plugin.RelaxTracker.IsActive &&
                    plugin.RelaxTracker.RecognitionSource == RelaxRecognitionSource.Automatic;
            }
        }

        /// <summary>Highest Relax event sequence emitted so far, or 0.</summary>
        public static long RelaxLatestEventSequence
        {
            get
            {
                CampmasterPlugin plugin = CampmasterPlugin.Instance;
                return plugin == null || plugin.RelaxTracker == null ? 0L : plugin.RelaxTracker.LatestSequence;
            }
        }

        /// <summary>Oldest Relax event sequence still retained, or 0 when history is empty.</summary>
        public static long RelaxOldestRetainedEventSequence
        {
            get
            {
                CampmasterPlugin plugin = CampmasterPlugin.Instance;
                return plugin == null || plugin.RelaxTracker == null ? 0L : plugin.RelaxTracker.OldestRetainedSequence;
            }
        }

        /// <summary>Highest deterministic living-camp event sequence emitted so far, or 0.</summary>
        public static long LivingLatestEventSequence
        {
            get
            {
                CampmasterPlugin plugin = CampmasterPlugin.Instance;
                return plugin == null || plugin.LivingTracker == null ? 0L : plugin.LivingTracker.LatestSequence;
            }
        }

        /// <summary>Oldest living-camp event sequence still retained, or 0 when history is empty.</summary>
        public static long LivingOldestRetainedEventSequence
        {
            get
            {
                CampmasterPlugin plugin = CampmasterPlugin.Instance;
                return plugin == null || plugin.LivingTracker == null ? 0L : plugin.LivingTracker.OldestRetainedSequence;
            }
        }

        /// <summary>Highest event sequence emitted so far, or 0.</summary>
        public static long LatestEventSequence
        {
            get
            {
                CampmasterPlugin plugin = CampmasterPlugin.Instance;
                return plugin == null || plugin.Tracker == null ? 0L : plugin.Tracker.LatestSequence;
            }
        }

        /// <summary>Oldest Hunt Camp event sequence still retained, or 0 when history is empty.</summary>
        public static long OldestRetainedEventSequence
        {
            get
            {
                CampmasterPlugin plugin = CampmasterPlugin.Instance;
                return plugin == null || plugin.Tracker == null ? 0L : plugin.Tracker.OldestRetainedSequence;
            }
        }

        /// <summary>
        /// Current camp state as a flat string dictionary. Keys are only
        /// present when the underlying fact is verified.
        /// </summary>
        public static Dictionary<string, string> GetCurrentSnapshot()
        {
            Dictionary<string, string> data = new Dictionary<string, string>(StringComparer.Ordinal);
            data["schemaVersion"] = SchemaVersion.ToString();
            data["source"] = "ErenshorCampmaster";

            CampmasterPlugin plugin = CampmasterPlugin.Instance;
            if (plugin == null || plugin.Tracker == null)
            {
                data["state"] = "Inactive";
                return data;
            }

            if (plugin.RelaxTracker != null && plugin.RelaxTracker.IsActive)
            {
                RelaxSnapshot relax;
                try { relax = plugin.RelaxTracker.BuildSnapshot(DateTime.UtcNow); }
                catch { data["state"] = "Unknown"; return data; }

                data["state"] = relax.State.ToString();
                data["mode"] = "Relax";
                data["activity"] = relax.State == RelaxSessionState.SuspendedForCombat ? "SuspendedForCombat" : "Quiet";
                data["authority"] = relax.Authority.ToString();
                data["recognition"] = relax.RecognitionSource == RelaxRecognitionSource.Automatic ? "Auto" : "Explicit";
                Put(data, "sessionId", relax.SessionId);
                Put(data, "zone", relax.Zone);
                if (relax.StartedUtc.HasValue) data["startedUtc"] = relax.StartedUtc.Value.ToString("o");
                data["elapsedSeconds"] = ((int)relax.ElapsedSeconds).ToString();
                if (relax.Anchor.HasValue)
                {
                    data["anchor"] = relax.Anchor.Value.Describe();
                    data["anchorOrigin"] = "explicit player position";
                }
                if (relax.Party != null && relax.Party.Count > 0)
                    data["party"] = string.Join(", ", relax.Party.ToArray());
                AddLivingSnapshot(data, plugin);
                return data;
            }

            CampSnapshot snap;
            try { snap = plugin.Tracker.BuildSnapshot(DateTime.UtcNow); }
            catch { data["state"] = "Unknown"; return data; }

            data["state"] = snap.State.ToString();
            data["mode"] = snap.IsActive ? "HuntCamp" : "None";
            data["recognition"] = snap.Source.ToString();
            data["activity"] = snap.Activity.ToString();
            data["authority"] = snap.Authority.ToString();
            Put(data, "sessionId", snap.SessionId);
            Put(data, "zone", snap.Zone);
            if (snap.StartedUtc.HasValue) data["startedUtc"] = snap.StartedUtc.Value.ToString("o");
            if (snap.IsActive) data["elapsedSeconds"] = ((int)snap.ElapsedSeconds).ToString();
            if (snap.Anchor.HasValue)
            {
                data["anchor"] = snap.Anchor.Value.Describe();
                Put(data, "anchorOrigin", snap.AnchorOrigin);
            }
            if (snap.Party != null && snap.Party.Count > 0) data["party"] = string.Join(", ", snap.Party.ToArray());

            PutRole(data, "mainTank", snap.MainTank);
            PutRole(data, "mainAssist", snap.MainAssist);
            PutRole(data, "designatedMainAssist", snap.DesignatedMainAssist);
            PutRole(data, "puller", snap.Puller);
            if (snap.CrowdControlKnown && snap.CrowdControl.Count > 0)
                data["crowdControl"] = string.Join(", ", snap.CrowdControl.ToArray());
            if (snap.HealersKnown && snap.Healers.Count > 0)
                data["healers"] = string.Join(", ", snap.Healers.ToArray());

            if (snap.AutoPullEnabled.HasValue) data["autoPullEnabled"] = snap.AutoPullEnabled.Value ? "true" : "false";
            if (snap.GroupPullModeEngaged.HasValue) data["groupPullModeEngaged"] = snap.GroupPullModeEngaged.Value ? "true" : "false";
            Put(data, "forcePullTarget", snap.ForcePullTargetName);
            Put(data, "currentPullTarget", snap.CurrentPullTargetName);
            if (snap.PullerActivelyPulling.HasValue) data["pullerActivelyPulling"] = snap.PullerActivelyPulling.Value ? "true" : "false";
            if (snap.HoldingForMana.HasValue) data["holdingForMana"] = snap.HoldingForMana.Value ? "true" : "false";
            if (snap.MaxPullLevelAbove.HasValue) data["maxPullLevelAbove"] = snap.MaxPullLevelAbove.Value.ToString();
            if (snap.MaxPullLevelBelow.HasValue) data["maxPullLevelBelow"] = snap.MaxPullLevelBelow.Value.ToString();
            if (snap.MaxPullDistance.HasValue) data["maxPullDistance"] = snap.MaxPullDistance.Value.ToString();
            if (snap.HoldManaFraction.HasValue)
                data["holdManaPercent"] = ((int)Math.Round(snap.HoldManaFraction.Value * 100f)).ToString();

            data["completedEncounters"] = snap.CompletedEncounters.ToString();
            data["verifiedPulls"] = snap.VerifiedPulls.ToString();
            data["roughEncounters"] = snap.RoughEncounters.ToString();
            data["repeatedEnemySignals"] = snap.RepeatedEnemySignals.ToString();
            data["partyChanges"] = snap.PartyChanges.ToString();
            data["suspensions"] = snap.Suspensions.ToString();
            AddLivingSnapshot(data, plugin);
            return data;
        }

        /// <summary>
        /// Sanitized factual context for optional social consumers. This is deliberately smaller
        /// than GetCurrentSnapshot: it reports camp/session/activity facts only and never publishes
        /// emotional conclusions about participants.
        /// </summary>
        public static Dictionary<string, string> GetCurrentSocialContext()
        {
            Dictionary<string, string> full = GetCurrentSnapshot();
            Dictionary<string, string> data = new Dictionary<string, string>(StringComparer.Ordinal);
            data["schemaVersion"] = SchemaVersion.ToString();
            data["socialContractVersion"] = SocialContractVersion.ToString();
            data["source"] = "ErenshorCampmaster";
            string[] allowed = new string[]
            {
                "state", "mode", "activity", "sessionId", "zone", "startedUtc", "elapsedSeconds", "party",
                "recognition", "livingMode", "livingPreparation", "livingSuspendedForCombat", "livingActivities"
            };
            for (int i = 0; i < allowed.Length; i++)
            {
                string value;
                if (full.TryGetValue(allowed[i], out value) && !string.IsNullOrWhiteSpace(value)) data[allowed[i]] = value;
            }
            CampmasterPlugin plugin = CampmasterPlugin.Instance;
            SocialActivitySnapshot activity = plugin == null ? null : plugin.SocialActivitySnapshot;
            if (activity != null)
            {
                data["activityState"] = activity.State.ToString();
                data["activityReason"] = string.IsNullOrWhiteSpace(activity.Reason) ? "unknown" : activity.Reason;
                data["secondsStationary"] = ((int)Math.Max(0.0, activity.SecondsStationary)).ToString();
                data["secondsOutOfCombat"] = ((int)Math.Max(0.0, activity.SecondsOutOfCombat)).ToString();
                data["secondsSinceMeaningfulGameplay"] = ((int)Math.Max(0.0, activity.SecondsSinceMeaningfulGameplay)).ToString();
                data["gameplayReady"] = activity.GameplayReady ? "true" : "false";
                data["autoRelaxActive"] = IsAutoRelaxActive ? "true" : "false";
            }
            return data;
        }

        /// <summary>
        /// Deterministic camp events newer than <paramref name="sequence"/>,
        /// oldest first. Pass 0 for everything currently retained.
        /// </summary>
        public static List<Dictionary<string, string>> GetEventsAfter(long sequence)
        {
            List<Dictionary<string, string>> result = new List<Dictionary<string, string>>();
            CampmasterPlugin plugin = CampmasterPlugin.Instance;
            if (plugin == null || plugin.Tracker == null) return result;

            List<CampEvent> events;
            try { events = plugin.Tracker.GetEventsAfter(sequence); }
            catch { return result; }

            for (int i = 0; i < events.Count; i++)
            {
                CampEvent evt = events[i];
                Dictionary<string, string> row = new Dictionary<string, string>(StringComparer.Ordinal);
                row["schemaVersion"] = SchemaVersion.ToString();
                row["socialContractVersion"] = SocialContractVersion.ToString();
                row["source"] = "ErenshorCampmaster";
                row["sequence"] = evt.Sequence.ToString();
                row["eventId"] = evt.EventId;
                row["utc"] = evt.Utc.ToString("o");
                row["mode"] = "HuntCamp";
                row["type"] = ToWireType(evt.Type);
                Put(row, "sessionId", evt.SessionId);
                Put(row, "zone", evt.Zone);
                Put(row, "detail", evt.Detail);
                if (evt.PartyNames != null && evt.PartyNames.Count > 0)
                    row["partyNames"] = string.Join(", ", evt.PartyNames.ToArray());
                if (evt.VerifiedFields != null)
                {
                    foreach (KeyValuePair<string, string> pair in evt.VerifiedFields)
                        if (!string.IsNullOrEmpty(pair.Key) && !string.IsNullOrEmpty(pair.Value))
                            row["verified." + pair.Key] = pair.Value;
                }
                result.Add(row);
            }
            return result;
        }

        /// <summary>
        /// Explicit Relax lifecycle events newer than <paramref name="sequence"/>, oldest first.
        /// Relax uses a separate sequence so existing Hunt Camp consumers remain compatible.
        /// </summary>
        public static List<Dictionary<string, string>> GetRelaxEventsAfter(long sequence)
        {
            List<Dictionary<string, string>> result = new List<Dictionary<string, string>>();
            CampmasterPlugin plugin = CampmasterPlugin.Instance;
            if (plugin == null || plugin.RelaxTracker == null) return result;

            List<RelaxEvent> events;
            try { events = plugin.RelaxTracker.GetEventsAfter(sequence); }
            catch { return result; }

            for (int i = 0; i < events.Count; i++)
            {
                RelaxEvent evt = events[i];
                Dictionary<string, string> row = new Dictionary<string, string>(StringComparer.Ordinal);
                row["schemaVersion"] = SchemaVersion.ToString();
                row["socialContractVersion"] = SocialContractVersion.ToString();
                row["source"] = "ErenshorCampmaster";
                row["sequence"] = evt.Sequence.ToString();
                row["eventId"] = evt.EventId;
                row["utc"] = evt.Utc.ToString("o");
                row["mode"] = "Relax";
                row["type"] = ToWireType(evt.Type);
                Put(row, "sessionId", evt.SessionId);
                Put(row, "zone", evt.Zone);
                Put(row, "detail", evt.Detail);
                if (evt.PartyNames != null && evt.PartyNames.Count > 0)
                    row["partyNames"] = string.Join(", ", evt.PartyNames.ToArray());
                result.Add(row);
            }
            return result;
        }

        /// <summary>Deterministic living-camp events newer than sequence, oldest first.</summary>
        public static List<Dictionary<string, string>> GetLivingEventsAfter(long sequence)
        {
            List<Dictionary<string, string>> result = new List<Dictionary<string, string>>();
            CampmasterPlugin plugin = CampmasterPlugin.Instance;
            if (plugin == null || plugin.LivingTracker == null) return result;
            List<CampLivingEvent> events;
            try { events = plugin.LivingTracker.GetEventsAfter(sequence); }
            catch { return result; }
            for (int i = 0; i < events.Count; i++)
            {
                CampLivingEvent evt = events[i];
                Dictionary<string, string> row = new Dictionary<string, string>(StringComparer.Ordinal);
                row["schemaVersion"] = SchemaVersion.ToString();
                row["livingContractVersion"] = "1";
                row["source"] = "ErenshorCampmaster";
                row["sequence"] = evt.Sequence.ToString();
                row["eventId"] = evt.EventId ?? string.Empty;
                row["utc"] = evt.Utc.ToString("o");
                row["mode"] = evt.Mode.ToString();
                row["type"] = ToWireType(evt.Type);
                row["meaningful"] = evt.Meaningful ? "true" : "false";
                Put(row, "sessionId", evt.SessionId);
                if (evt.StableId >= 0) row["participantId"] = evt.StableId.ToString();
                Put(row, "participantName", evt.ParticipantName);
                Put(row, "detail", evt.Detail);
                Put(row, "counterpart", evt.Counterpart);
                Put(row, "subjectCategory", evt.SubjectCategory);
                Put(row, "subjectSource", evt.SubjectSource);
                row["presentationCategory"] = evt.PresentationCategory.ToString().ToLowerInvariant();
                result.Add(row);
            }
            return result;
        }

        private static void AddLivingSnapshot(Dictionary<string, string> data, CampmasterPlugin plugin)
        {
            if (data == null || plugin == null || plugin.LivingTracker == null) return;
            CampLivingSnapshot living;
            try { living = plugin.LivingTracker.BuildSnapshot(); } catch { return; }
            data["livingContractVersion"] = "1";
            data["socialContractVersion"] = SocialContractVersion.ToString();
            data["livingMode"] = living.Mode.ToString();
            data["livingPreparation"] = living.Preparation.ToString();
            data["livingSuspendedForCombat"] = living.SuspendedForCombat ? "true" : "false";
            if (living.Activities.Count > 0)
            {
                string activities = string.Empty;
                for (int i = 0; i < living.Activities.Count; i++)
                {
                    CampLivingActivity activity = living.Activities[i];
                    if (activity == null) continue;
                    if (activities.Length > 0) activities += "; ";
                    activities += (activity.ParticipantName ?? ("Sim #" + activity.StableId.ToString())) + "=" + CampLivingActivityTracker.DescribeActivity(activity.Type);
                }
                Put(data, "livingActivities", activities);
            }
        }

        private static string ToWireType(CampLivingEventType type)
        {
            switch (type)
            {
                case CampLivingEventType.SessionStarted: return "camp_living_started";
                case CampLivingEventType.SessionEnded: return "camp_living_ended";
                case CampLivingEventType.ActivityStarted: return "camp_activity_started";
                case CampLivingEventType.ActivityCompleted: return "camp_activity_completed";
                case CampLivingEventType.ActivityInterrupted: return "camp_activity_interrupted";
                case CampLivingEventType.CombatSuspended: return "camp_activities_suspended_combat";
                case CampLivingEventType.CombatResumed: return "camp_activities_resumed";
                case CampLivingEventType.WatchEvent: return "camp_watch_event";
                case CampLivingEventType.MinorDisagreement: return "camp_minor_disagreement";
                case CampLivingEventType.PreparationChanged: return "camp_preparation_changed";
                default: return "camp_living_unknown";
            }
        }

        private static string ToWireType(RelaxEventType type)
        {
            switch (type)
            {
                case RelaxEventType.RelaxStarted: return "relax_started";
                case RelaxEventType.RelaxSuspended: return "relax_suspended";
                case RelaxEventType.RelaxResumed: return "relax_resumed";
                case RelaxEventType.RelaxEnded: return "relax_ended";
                default: return "relax_unknown";
            }
        }

        private static string ToWireType(CampEventType type)
        {
            switch (type)
            {
                case CampEventType.CampStarted: return "camp_started";
                case CampEventType.CampEnded: return "camp_ended";
                case CampEventType.CampSuspended: return "camp_suspended";
                case CampEventType.CampResumed: return "camp_resumed";
                case CampEventType.CampPartyChanged: return "camp_party_changed";
                case CampEventType.CampPullStarted: return "camp_pull_started";
                case CampEventType.CampEncounterStarted: return "camp_encounter_started";
                case CampEventType.CampEncounterCompleted: return "camp_encounter_completed";
                case CampEventType.CampRecoveryStarted: return "camp_recovery_started";
                case CampEventType.CampRecoveryEnded: return "camp_recovery_ended";
                case CampEventType.CampRepeatedEnemy: return "camp_repeated_enemy";
                case CampEventType.CampRoughEncounter: return "camp_rough_encounter";
                case CampEventType.CampRoleSnapshot: return "camp_role_snapshot";
                default: return "camp_unknown";
            }
        }

        private static void Put(Dictionary<string, string> data, string key, string value)
        {
            if (!string.IsNullOrEmpty(value)) data[key] = value;
        }

        private static void PutRole(Dictionary<string, string> data, string key, RoleHolder role)
        {
            if (!role.IsResolved) return;                 // unknown stays absent
            data[key] = role.Describe();                  // verified None is explicit
            data[key + "Kind"] = role.Kind.ToString();
        }
    }
}
