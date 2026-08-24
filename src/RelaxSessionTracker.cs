using System;
using System.Collections.Generic;

namespace ErenshorCampmaster
{
    // Pure deterministic Relax lifecycle. It never calls Unity/game APIs and
    // never moves, guards, heals, or otherwise controls the party.
    internal sealed class RelaxSessionTracker
    {
        private const int MaxEvents = 64;

        private readonly RelaxConfig _config;
        private readonly List<RelaxEvent> _events = new List<RelaxEvent>();
        private RelaxSessionState _state = RelaxSessionState.Inactive;
        private string _sessionId;
        private string _zone;
        private DateTime? _startedUtc;
        private CampVector3? _anchor;
        private List<string> _party = new List<string>();
        private CampAuthority _authority = CampAuthority.Unknown;
        private RelaxRecognitionSource _recognitionSource = RelaxRecognitionSource.Explicit;
        private long _latestSequence;
        private int _eventOrdinal;
        private CampVector3? _pendingStartAnchor;
        private bool _pendingStart;
        private bool _pendingStop;
        private string _pendingStopReason;
        private DateTime? _outsideSinceUtc;
        private DateTime? _partyMissingSinceUtc;

        internal RelaxSessionTracker(RelaxConfig config)
        {
            _config = config ?? new RelaxConfig();
        }

        internal bool IsActive
        {
            get { return _state == RelaxSessionState.Active || _state == RelaxSessionState.SuspendedForCombat; }
        }

        internal RelaxSessionState State { get { return _state; } }
        internal RelaxRecognitionSource RecognitionSource { get { return _recognitionSource; } }
        internal long LatestSequence { get { return _latestSequence; } }
        internal long OldestRetainedSequence { get { return _events.Count == 0 ? 0L : _events[0].Sequence; } }

        internal bool PromoteAutomaticToExplicit()
        {
            if (!IsActive || _recognitionSource != RelaxRecognitionSource.Automatic) return false;
            _recognitionSource = RelaxRecognitionSource.Explicit;
            return true;
        }

        internal void RequestStart(CampVector3? anchor)
        {
            RequestStart(anchor, RelaxRecognitionSource.Explicit);
        }

        internal void RequestStart(CampVector3? anchor, RelaxRecognitionSource source)
        {
            _pendingStartAnchor = anchor;
            _recognitionSource = source;
            _pendingStart = true;
            _pendingStop = false;
        }

        internal void RequestStop()
        {
            RequestStop("player ended Relax");
        }

        internal void RequestStop(string reason)
        {
            _pendingStop = true;
            _pendingStopReason = string.IsNullOrWhiteSpace(reason) ? "player ended Relax" : reason.Trim();
            _pendingStart = false;
        }

        internal void Tick(CampObservation obs, DateTime nowUtc)
        {
            if (_pendingStop)
            {
                _pendingStop = false;
                string reason = _pendingStopReason;
                _pendingStopReason = null;
                if (IsActive) End(nowUtc, string.IsNullOrEmpty(reason) ? "player ended Relax" : reason);
            }

            if (_pendingStart)
            {
                _pendingStart = false;
                if (!IsActive && CanStart(obs) && (_pendingStartAnchor.HasValue || obs.PlayerPosition.HasValue))
                    Start(obs, nowUtc, _pendingStartAnchor);
                _pendingStartAnchor = null;
            }

            if (!IsActive || obs == null || !obs.ReadSucceeded) return;

            if (!string.IsNullOrWhiteSpace(_zone) && !string.IsNullOrWhiteSpace(obs.Zone) &&
                !string.Equals(_zone, obs.Zone, StringComparison.OrdinalIgnoreCase))
            {
                End(nowUtc, "zone changed");
                return;
            }

            if (!obs.HasParty || obs.LocalResolvedMembers <= 0)
            {
                if (!_partyMissingSinceUtc.HasValue) _partyMissingSinceUtc = nowUtc;
                if ((nowUtc - _partyMissingSinceUtc.Value).TotalSeconds >= Math.Max(0.0, _config.PartyLossGraceSeconds))
                {
                    End(nowUtc, "local party was unavailable beyond the Relax grace period");
                    return;
                }
            }
            else
            {
                _partyMissingSinceUtc = null;
                _party = Copy(obs.PartyNames);
                _authority = obs.Authority;
            }

            if (_anchor.HasValue && obs.PlayerPosition.HasValue)
            {
                float distance = CampVector3.Distance(_anchor.Value, obs.PlayerPosition.Value);
                if (distance > Math.Max(1f, _config.DepartureRadius))
                {
                    if (!_outsideSinceUtc.HasValue) _outsideSinceUtc = nowUtc;
                    if ((nowUtc - _outsideSinceUtc.Value).TotalSeconds >= Math.Max(0.0, _config.DepartureGraceSeconds))
                    {
                        End(nowUtc, "player left the Relax anchor area");
                        return;
                    }
                }
                else _outsideSinceUtc = null;
            }
            else
            {
                _outsideSinceUtc = null;
            }

            if (!obs.InCombat.HasValue) return;

            if (obs.InCombat.Value && _state == RelaxSessionState.Active)
            {
                _state = RelaxSessionState.SuspendedForCombat;
                Emit(RelaxEventType.RelaxSuspended, nowUtc, "verified combat started");
                return;
            }

            if (!obs.InCombat.Value && _state == RelaxSessionState.SuspendedForCombat && IsAtAnchor(obs))
            {
                _state = RelaxSessionState.Active;
                Emit(RelaxEventType.RelaxResumed, nowUtc, "combat cleared while the party remained at the Relax location");
            }
        }

        internal RelaxSnapshot BuildSnapshot(DateTime nowUtc)
        {
            RelaxSnapshot snap = new RelaxSnapshot();
            snap.SessionId = _sessionId;
            snap.State = _state;
            snap.Zone = _zone;
            snap.StartedUtc = _startedUtc;
            snap.Anchor = _anchor;
            snap.Party = Copy(_party);
            snap.Authority = _authority;
            snap.RecognitionSource = _recognitionSource;
            if (_startedUtc.HasValue && IsActive)
                snap.ElapsedSeconds = Math.Max(0.0, (nowUtc - _startedUtc.Value).TotalSeconds);
            return snap;
        }

        internal List<RelaxEvent> GetEventsAfter(long sequence)
        {
            List<RelaxEvent> result = new List<RelaxEvent>();
            for (int i = 0; i < _events.Count; i++)
                if (_events[i].Sequence > sequence) result.Add(Copy(_events[i]));
            return result;
        }

        private static bool CanStart(CampObservation obs)
        {
            if (obs == null || !obs.ReadSucceeded || !obs.HasParty || obs.LocalResolvedMembers <= 0) return false;
            if (!obs.InCombat.HasValue || obs.InCombat.Value) return false;
            return true;
        }

        private bool IsAtAnchor(CampObservation obs)
        {
            if (!_anchor.HasValue || obs == null || !obs.PlayerPosition.HasValue) return false;
            return CampVector3.Distance(_anchor.Value, obs.PlayerPosition.Value) <= Math.Max(1f, _config.DepartureRadius);
        }

        private void Start(CampObservation obs, DateTime nowUtc, CampVector3? requestedAnchor)
        {
            _state = RelaxSessionState.Active;
            _startedUtc = nowUtc;
            _zone = obs.Zone;
            _anchor = requestedAnchor ?? obs.PlayerPosition;
            _party = Copy(obs.PartyNames);
            _authority = obs.Authority;
            _outsideSinceUtc = null;
            _partyMissingSinceUtc = null;
            _sessionId = "relax-" + nowUtc.ToString("yyyyMMddHHmmss") + "-" + (++_eventOrdinal).ToString();
            Emit(RelaxEventType.RelaxStarted, nowUtc, _recognitionSource == RelaxRecognitionSource.Automatic
                ? "safe stationary downtime entered automatic Relax"
                : "player explicitly chose Relax Here");
        }

        private void End(DateTime nowUtc, string detail)
        {
            Emit(RelaxEventType.RelaxEnded, nowUtc, detail);
            _state = RelaxSessionState.Inactive;
            _sessionId = null;
            _zone = null;
            _startedUtc = null;
            _anchor = null;
            _party.Clear();
            _authority = CampAuthority.Unknown;
            _recognitionSource = RelaxRecognitionSource.Explicit;
            _outsideSinceUtc = null;
            _partyMissingSinceUtc = null;
        }

        private void Emit(RelaxEventType type, DateTime nowUtc, string detail)
        {
            RelaxEvent evt = new RelaxEvent();
            evt.Sequence = ++_latestSequence;
            evt.EventId = "relax-event-" + evt.Sequence.ToString();
            evt.Utc = nowUtc;
            evt.SessionId = _sessionId;
            evt.Type = type;
            evt.Zone = _zone;
            evt.Detail = detail ?? string.Empty;
            evt.RecognitionSource = _recognitionSource;
            evt.PartyNames = Copy(_party);
            _events.Add(evt);
            while (_events.Count > MaxEvents) _events.RemoveAt(0);
        }

        private static List<string> Copy(IList<string> values)
        {
            List<string> result = new List<string>();
            if (values == null) return result;
            for (int i = 0; i < values.Count; i++)
                if (!string.IsNullOrWhiteSpace(values[i])) result.Add(values[i]);
            return result;
        }

        private static RelaxEvent Copy(RelaxEvent source)
        {
            RelaxEvent evt = new RelaxEvent();
            evt.Sequence = source.Sequence;
            evt.EventId = source.EventId;
            evt.Utc = source.Utc;
            evt.SessionId = source.SessionId;
            evt.Type = source.Type;
            evt.Zone = source.Zone;
            evt.Detail = source.Detail;
            evt.PartyNames = Copy(source.PartyNames);
            evt.RecognitionSource = source.RecognitionSource;
            return evt;
        }
    }
}
