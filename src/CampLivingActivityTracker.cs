using System;
using System.Collections.Generic;

namespace ErenshorCampmaster
{
    // Deterministic party-camp activity loop. It owns only Campmaster context and event output.
    // Native Erenshor combat remains the interrupt authority; no actor methods are called here.
    internal sealed class CampLivingActivityTracker
    {
        internal const int MaxEvents = 96;
        private readonly List<CampLivingActivity> _activities = new List<CampLivingActivity>();
        private readonly List<CampLivingEvent> _events = new List<CampLivingEvent>();
        private string _sessionId;
        private CampLivingMode _mode;
        private long _sequence;
        private int _cycle;
        private int _preparation;
        private bool _suspendedForCombat;

        internal long LatestSequence { get { return _sequence; } }
        internal long OldestRetainedSequence { get { return _events.Count == 0 ? 0L : _events[0].Sequence; } }
        internal bool IsActive { get { return _mode != CampLivingMode.None && !string.IsNullOrEmpty(_sessionId); } }
        internal int ActiveActivityCount { get { return _activities.Count; } }

        internal void Tick(CampLivingMode mode, string sessionId, bool active, CampObservation observation, DateTime nowUtc)
        {
            nowUtc = NormalizeUtc(nowUtc);
            sessionId = sessionId ?? string.Empty;
            if (!active || mode == CampLivingMode.None || sessionId.Length == 0)
            {
                if (IsActive) Stop(nowUtc, "camp session ended");
                return;
            }

            if (!IsActive || _mode != mode || !string.Equals(_sessionId, sessionId, StringComparison.Ordinal))
            {
                if (IsActive) Stop(nowUtc, "camp session changed");
                Start(mode, sessionId, nowUtc);
            }

            // A failed native read cannot prove party eligibility. Release runtime ownership and
            // wait for a good sample rather than simulating through unknown state.
            if (observation == null || !observation.ReadSucceeded)
            {
                InterruptAll(nowUtc, "native party state unreadable");
                return;
            }

            if (observation.InCombat == true)
            {
                if (!_suspendedForCombat)
                {
                    InterruptAll(nowUtc, "combat took priority");
                    _suspendedForCombat = true;
                    AddEvent(nowUtc, CampLivingEventType.CombatSuspended, -1, null, "Camp activities suspended for combat.", true);
                }
                return;
            }

            if (_suspendedForCombat)
            {
                _suspendedForCombat = false;
                AddEvent(nowUtc, CampLivingEventType.CombatResumed, -1, null, "Camp activities resumed after combat.", true);
            }

            // Hunt Camp's native puller has higher priority than contextual camp routines even
            // before combat formally begins. Do not show Sims training/eating while Erenshor is
            // actively sending the puller out. This still does not call or alter any native AI.
            if (_mode == CampLivingMode.HuntCamp && observation.PullerActivelyPulling == true)
            {
                InterruptAll(nowUtc, "native pull took priority");
                return;
            }

            ReconcileParticipants(observation, nowUtc);
            CompleteDue(nowUtc);
            AssignMissing(observation, nowUtc);
        }

        internal void Stop(DateTime nowUtc, string reason)
        {
            nowUtc = NormalizeUtc(nowUtc);
            if (!IsActive)
            {
                _activities.Clear();
                _suspendedForCombat = false;
                return;
            }
            InterruptAll(nowUtc, string.IsNullOrEmpty(reason) ? "camp ownership ended" : reason);
            AddEvent(nowUtc, CampLivingEventType.SessionEnded, -1, null,
                "Living camp ended: " + (string.IsNullOrEmpty(reason) ? "session ended" : reason) + ".", false);
            _sessionId = null;
            _mode = CampLivingMode.None;
            _cycle = 0;
            _preparation = 0;
            _suspendedForCombat = false;
        }

        internal CampLivingSnapshot BuildSnapshot()
        {
            CampLivingSnapshot snapshot = new CampLivingSnapshot();
            snapshot.SessionId = _sessionId;
            snapshot.Mode = _mode;
            snapshot.SuspendedForCombat = _suspendedForCombat;
            snapshot.Preparation = _preparation;
            for (int i = 0; i < _activities.Count; i++) snapshot.Activities.Add(Clone(_activities[i]));
            int start = Math.Max(0, _events.Count - 8);
            for (int i = start; i < _events.Count; i++) snapshot.RecentEvents.Add(Clone(_events[i]));
            return snapshot;
        }

        internal List<CampLivingEvent> GetEventsAfter(long sequence)
        {
            List<CampLivingEvent> result = new List<CampLivingEvent>();
            for (int i = 0; i < _events.Count; i++)
                if (_events[i].Sequence > sequence) result.Add(Clone(_events[i]));
            return result;
        }

        internal static string DescribeActivity(CampLivingActivityType type)
        {
            switch (type)
            {
                case CampLivingActivityType.Resting: return "Resting";
                case CampLivingActivityType.Eating: return "Eating";
                case CampLivingActivityType.EquipmentCare: return "Tending equipment";
                case CampLivingActivityType.Training: return "Training";
                case CampLivingActivityType.Watch: return "Standing watch";
                case CampLivingActivityType.Socializing: return "Socializing";
                case CampLivingActivityType.PreparingTravel: return "Preparing for travel";
                case CampLivingActivityType.Quiet: return "Keeping quiet";
                default: return "Camp activity";
            }
        }

        private void Start(CampLivingMode mode, string sessionId, DateTime nowUtc)
        {
            _mode = mode;
            _sessionId = sessionId;
            _cycle = 0;
            _preparation = 0;
            _suspendedForCombat = false;
            _activities.Clear();
            AddEvent(nowUtc, CampLivingEventType.SessionStarted, -1, null,
                mode == CampLivingMode.Relax ? "The party settled into a Relax camp." : "The party settled into Hunt Camp routines.", true);
        }

        private void ReconcileParticipants(CampObservation observation, DateTime nowUtc)
        {
            for (int i = _activities.Count - 1; i >= 0; i--)
            {
                CampLivingActivity activity = _activities[i];
                CampParticipantObservation participant = FindParticipant(observation, activity.StableId);
                if (participant != null && participant.LocalUsable && !participant.KnownDead && !participant.Remote) continue;
                _activities.RemoveAt(i);
                activity.State = CampLivingActivityState.Interrupted;
                activity.Outcome = "participant became unavailable";
                AddEvent(nowUtc, CampLivingEventType.ActivityInterrupted, activity.StableId, activity.ParticipantName,
                    activity.ParticipantName + " stopped " + DescribeActivity(activity.Type).ToLowerInvariant() + " because the participant became unavailable.", false);
            }
        }

        private void CompleteDue(DateTime nowUtc)
        {
            for (int i = _activities.Count - 1; i >= 0; i--)
            {
                CampLivingActivity activity = _activities[i];
                if (activity == null || activity.EndsUtc > nowUtc) continue;
                _activities.RemoveAt(i);
                activity.State = CampLivingActivityState.Completed;
                Complete(activity, nowUtc);
            }
        }

        private void AssignMissing(CampObservation observation, DateTime nowUtc)
        {
            List<CampParticipantObservation> eligible = EligibleParticipants(observation);
            bool watchAssigned = HasWatch();
            for (int i = 0; i < eligible.Count; i++)
            {
                CampParticipantObservation participant = eligible[i];
                if (HasActivity(participant.StableId)) continue;
                _cycle++;
                int hash = PositiveHash((_sessionId ?? string.Empty) + "|" + _mode.ToString() + "|" + _cycle.ToString() + "|" + participant.StableId.ToString());
                CampLivingActivityType type;
                if (!watchAssigned)
                {
                    type = CampLivingActivityType.Watch;
                    watchAssigned = true;
                }
                else type = SelectType(hash, _mode);

                CampLivingActivity activity = new CampLivingActivity();
                activity.StableId = participant.StableId;
                activity.ParticipantName = participant.Name;
                activity.Type = type;
                activity.State = CampLivingActivityState.Active;
                activity.StartedUtc = nowUtc;
                activity.EndsUtc = nowUtc.AddSeconds(35 + (hash % 31));
                activity.Ordinal = _cycle;
                _activities.Add(activity);
                AddEvent(nowUtc, CampLivingEventType.ActivityStarted, activity.StableId, activity.ParticipantName,
                    activity.ParticipantName + " began " + DescribeActivity(activity.Type).ToLowerInvariant() + ".", false);
            }
        }

        private void Complete(CampLivingActivity activity, DateTime nowUtc)
        {
            int hash = PositiveHash((_sessionId ?? string.Empty) + "|complete|" + activity.StableId.ToString() + "|" + activity.Ordinal.ToString() + "|" + ((int)activity.Type).ToString());
            string detail;
            bool meaningful = false;
            int preparationDelta = 0;
            CampLivingEventType eventType = CampLivingEventType.ActivityCompleted;

            switch (activity.Type)
            {
                case CampLivingActivityType.Watch:
                    if ((hash % 5) == 0)
                    {
                        detail = activity.ParticipantName + " noticed signs of movement near camp while on watch.";
                        eventType = CampLivingEventType.WatchEvent;
                        meaningful = true;
                    }
                    else detail = activity.ParticipantName + " finished a quiet watch.";
                    break;
                case CampLivingActivityType.Training:
                    detail = activity.ParticipantName + " finished a short training drill.";
                    preparationDelta = 1;
                    break;
                case CampLivingActivityType.EquipmentCare:
                    detail = activity.ParticipantName + " finished tending equipment.";
                    preparationDelta = 1;
                    break;
                case CampLivingActivityType.PreparingTravel:
                    detail = activity.ParticipantName + " finished preparing for travel.";
                    preparationDelta = 2;
                    meaningful = true;
                    break;
                case CampLivingActivityType.Socializing:
                    if ((hash % 7) == 0)
                    {
                        detail = BuildMinorDisagreement(activity.ParticipantName, _preparation, hash);
                        eventType = CampLivingEventType.MinorDisagreement;
                        meaningful = true;
                    }
                    else detail = activity.ParticipantName + " spent a while socializing around camp.";
                    break;
                case CampLivingActivityType.Eating:
                    detail = activity.ParticipantName + " finished a simple camp meal.";
                    break;
                case CampLivingActivityType.Resting:
                    detail = activity.ParticipantName + " finished resting.";
                    break;
                default:
                    detail = activity.ParticipantName + " spent some quiet time at camp.";
                    break;
            }

            activity.Outcome = detail;
            CampLivingEvent completed = AddEvent(nowUtc, eventType, activity.StableId, activity.ParticipantName, detail, meaningful);
            if (eventType == CampLivingEventType.MinorDisagreement)
            {
                completed.Counterpart = "group";
                if (detail.IndexOf("preparations were sufficient", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    completed.SubjectCategory = "preparation";
                    completed.SubjectSource = "current_preparation_state";
                }
            }
            if (preparationDelta > 0)
            {
                int before = _preparation;
                _preparation = Math.Min(12, _preparation + preparationDelta);
                if (_preparation != before)
                    AddEvent(nowUtc, CampLivingEventType.PreparationChanged, activity.StableId, activity.ParticipantName,
                        "Campmaster preparation increased to " + _preparation.ToString() + "/12. This is Campmaster context, not a native Erenshor buff.",
                        _preparation == 6 || _preparation == 12);
            }
        }

        private void InterruptAll(DateTime nowUtc, string reason)
        {
            for (int i = _activities.Count - 1; i >= 0; i--)
            {
                CampLivingActivity activity = _activities[i];
                _activities.RemoveAt(i);
                if (activity == null) continue;
                activity.State = CampLivingActivityState.Interrupted;
                activity.Outcome = reason;
                AddEvent(nowUtc, CampLivingEventType.ActivityInterrupted, activity.StableId, activity.ParticipantName,
                    activity.ParticipantName + " stopped " + DescribeActivity(activity.Type).ToLowerInvariant() + ": " + reason + ".", false);
            }
        }

        private List<CampParticipantObservation> EligibleParticipants(CampObservation observation)
        {
            List<CampParticipantObservation> result = new List<CampParticipantObservation>();
            if (observation == null || observation.Participants == null) return result;
            for (int i = 0; i < observation.Participants.Count; i++)
            {
                CampParticipantObservation value = observation.Participants[i];
                if (value == null || value.StableId < 0 || string.IsNullOrEmpty(value.Name)) continue;
                if (!value.LocalUsable || value.Remote || value.KnownDead) continue;
                result.Add(value);
            }
            result.Sort(delegate(CampParticipantObservation a, CampParticipantObservation b)
            {
                int cmp = a.StableId.CompareTo(b.StableId);
                return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        private static CampParticipantObservation FindParticipant(CampObservation observation, int stableId)
        {
            if (observation == null || observation.Participants == null) return null;
            for (int i = 0; i < observation.Participants.Count; i++)
            {
                CampParticipantObservation value = observation.Participants[i];
                if (value != null && value.StableId == stableId) return value;
            }
            return null;
        }

        private bool HasActivity(int stableId)
        {
            for (int i = 0; i < _activities.Count; i++) if (_activities[i] != null && _activities[i].StableId == stableId) return true;
            return false;
        }

        private bool HasWatch()
        {
            for (int i = 0; i < _activities.Count; i++)
                if (_activities[i] != null && _activities[i].Type == CampLivingActivityType.Watch) return true;
            return false;
        }

        private static CampLivingActivityType SelectType(int hash, CampLivingMode mode)
        {
            if (mode == CampLivingMode.HuntCamp)
            {
                CampLivingActivityType[] hunt = new CampLivingActivityType[]
                {
                    CampLivingActivityType.Resting, CampLivingActivityType.EquipmentCare,
                    CampLivingActivityType.PreparingTravel, CampLivingActivityType.Quiet
                };
                return hunt[hash % hunt.Length];
            }
            CampLivingActivityType[] relax = new CampLivingActivityType[]
            {
                CampLivingActivityType.Resting, CampLivingActivityType.Eating, CampLivingActivityType.EquipmentCare,
                CampLivingActivityType.Training, CampLivingActivityType.Socializing, CampLivingActivityType.PreparingTravel,
                CampLivingActivityType.Quiet
            };
            return relax[hash % relax.Length];
        }

        private CampLivingEvent AddEvent(DateTime utc, CampLivingEventType type, int stableId, string name, string detail, bool meaningful)
        {
            _sequence++;
            CampLivingEvent evt = new CampLivingEvent();
            evt.Sequence = _sequence;
            evt.EventId = ((_sessionId ?? "camp-session") + "-living-" + _sequence.ToString());
            evt.Utc = NormalizeUtc(utc);
            evt.SessionId = _sessionId;
            evt.Mode = _mode;
            evt.Type = type;
            evt.StableId = stableId;
            evt.ParticipantName = name;
            evt.Detail = Clean(detail, 320);
            evt.Meaningful = meaningful;
            evt.PresentationCategory = PresentationFor(type);
            _events.Add(evt);
            while (_events.Count > MaxEvents) _events.RemoveAt(0);
            return evt;
        }

        internal static CampPresentationCategory PresentationFor(CampLivingEventType type)
        {
            if (type == CampLivingEventType.WatchEvent) return CampPresentationCategory.Informational;
            if (type == CampLivingEventType.MinorDisagreement || type == CampLivingEventType.CombatSuspended)
                return CampPresentationCategory.Warning;
            return CampPresentationCategory.Neutral;
        }

        internal static string BuildMinorDisagreement(string participantName, int preparation, int deterministicHash)
        {
            string who = string.IsNullOrWhiteSpace(participantName) ? "A party member" : participantName.Trim();
            bool preparationSubject = preparation > 0 && ((deterministicHash / 7) % 2) == 0;
            return preparationSubject
                ? who + " had a minor disagreement with the group about whether camp preparations were sufficient."
                : who + " had a minor disagreement with the group.";
        }

        private static CampLivingActivity Clone(CampLivingActivity source)
        {
            CampLivingActivity value = new CampLivingActivity();
            if (source == null) return value;
            value.StableId = source.StableId; value.ParticipantName = source.ParticipantName; value.Type = source.Type;
            value.State = source.State; value.StartedUtc = source.StartedUtc; value.EndsUtc = source.EndsUtc; value.Outcome = source.Outcome; value.Ordinal = source.Ordinal;
            return value;
        }

        private static CampLivingEvent Clone(CampLivingEvent source)
        {
            CampLivingEvent value = new CampLivingEvent();
            if (source == null) return value;
            value.Sequence = source.Sequence; value.EventId = source.EventId; value.Utc = source.Utc; value.SessionId = source.SessionId;
            value.Mode = source.Mode; value.Type = source.Type; value.StableId = source.StableId; value.ParticipantName = source.ParticipantName;
            value.Detail = source.Detail; value.Meaningful = source.Meaningful; value.Counterpart = source.Counterpart;
            value.SubjectCategory = source.SubjectCategory; value.SubjectSource = source.SubjectSource;
            value.PresentationCategory = source.PresentationCategory;
            return value;
        }

        private static int PositiveHash(string text)
        {
            unchecked
            {
                int hash = 19; string value = text ?? string.Empty;
                for (int i = 0; i < value.Length; i++) hash = (hash * 31) + value[i];
                if (hash == int.MinValue) return int.MaxValue;
                return Math.Abs(hash);
            }
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value == default(DateTime)) return DateTime.UtcNow;
            if (value.Kind == DateTimeKind.Utc) return value;
            try { return value.ToUniversalTime(); } catch { return DateTime.UtcNow; }
        }

        private static string Clean(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string clean = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Replace('\0', ' ').Trim();
            return clean.Length <= max ? clean : clean.Substring(0, max);
        }
    }
}
