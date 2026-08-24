using System;

namespace ErenshorCampmaster
{
    internal static class NativeMeaningfulActivityPolicy
    {
        internal static bool Resolve(bool? pullerActivelyPulling, string currentPullTarget,
            string forcedPullTarget, out string reason)
        {
            reason = pullerActivelyPulling == true ? "native_pull_active" : string.Empty;
            // Target references are deliberately ignored: both can remain populated after the
            // action that wrote them, while CurrentPullPhase is the native current-operation signal.
            return pullerActivelyPulling == true;
        }
    }

    internal enum CampSocialActivityState
    {
        ActiveGameplay = 0,
        Travel = 1,
        Combat = 2,
        SocialDowntime = 3,
        ExtendedDowntime = 4
    }

    internal sealed class SocialActivityConfig
    {
        internal float MovementRadius = 3f;
        internal double TravelHoldSeconds = 6.0;
        internal double SocialDowntimeSeconds = 60.0;
        internal double ExtendedDowntimeSeconds = 240.0;
    }

    internal sealed class SocialActivitySnapshot
    {
        internal CampSocialActivityState State = CampSocialActivityState.ActiveGameplay;
        internal double SecondsStationary;
        internal double SecondsOutOfCombat;
        internal double SecondsSinceMeaningfulGameplay;
        internal string Zone;
        internal bool GameplayReady;
        internal bool SafeForAutoRelax;
        internal string Reason = "initializing";
    }

    // Pure, one-sample-per-second activity authority. It derives only from supplied native facts and
    // position displacement. It never calls Unity, scans a scene, or controls an actor.
    internal sealed class SocialActivityTracker
    {
        private readonly SocialActivityConfig _config;
        private CampVector3? _stationaryAnchor;
        private DateTime? _stationarySinceUtc;
        private DateTime? _outOfCombatSinceUtc;
        private DateTime? _lastMeaningfulGameplayUtc;
        private DateTime? _lastMovementUtc;
        private string _zone;
        private CampSocialActivityState _state = CampSocialActivityState.ActiveGameplay;

        internal SocialActivityTracker(SocialActivityConfig config)
        {
            _config = config ?? new SocialActivityConfig();
        }

        internal CampSocialActivityState State { get { return _state; } }

        internal SocialActivitySnapshot Tick(CampObservation obs, DateTime nowUtc)
        {
            nowUtc = NormalizeUtc(nowUtc);
            bool ready = obs != null && obs.ReadSucceeded && obs.GameplayReady && obs.HasParty &&
                obs.LocalResolvedMembers > 0 && obs.PlayerPosition.HasValue && obs.InCombat.HasValue;
            string competitiveReason = obs == null ? string.Empty : obs.PvpActive == true ? "pvp_active" :
                obs.DuelActive == true ? "duel_active" : !obs.PvpActive.HasValue ? "pvp_state_unknown" :
                !obs.DuelActive.HasValue ? "duel_state_unknown" : string.Empty;
            bool competitive = competitiveReason.Length > 0;
            bool zoneChanged = obs != null && !string.IsNullOrWhiteSpace(_zone) &&
                !string.IsNullOrWhiteSpace(obs.Zone) && !string.Equals(_zone, obs.Zone, StringComparison.OrdinalIgnoreCase);

            if (!ready || competitive || zoneChanged)
            {
                ResetMotion(obs, nowUtc);
                _state = CampSocialActivityState.ActiveGameplay;
                return Build(obs, nowUtc, false, !ready ? "gameplay_not_ready" : competitive ? competitiveReason : "scene_changed");
            }

            _zone = obs.Zone;
            if (obs.InCombat == true)
            {
                _state = CampSocialActivityState.Combat;
                _outOfCombatSinceUtc = null;
                _lastMeaningfulGameplayUtc = nowUtc;
                _stationarySinceUtc = nowUtc;
                _stationaryAnchor = obs.PlayerPosition;
                return Build(obs, nowUtc, false, "verified_combat");
            }
            if (!_outOfCombatSinceUtc.HasValue) _outOfCombatSinceUtc = nowUtc;

            if (obs.MeaningfulGameplayActivity)
            {
                _lastMeaningfulGameplayUtc = nowUtc;
                _stationarySinceUtc = nowUtc;
                _stationaryAnchor = obs.PlayerPosition;
                _state = CampSocialActivityState.ActiveGameplay;
                return Build(obs, nowUtc, false, string.IsNullOrWhiteSpace(obs.MeaningfulGameplayReason)
                    ? "meaningful_gameplay" : obs.MeaningfulGameplayReason);
            }

            if (!_stationaryAnchor.HasValue)
            {
                _stationaryAnchor = obs.PlayerPosition;
                _stationarySinceUtc = nowUtc;
                _lastMeaningfulGameplayUtc = nowUtc;
            }

            float movementRadius = Math.Max(0.5f, _config.MovementRadius);
            bool moved = CampVector3.Distance(_stationaryAnchor.Value, obs.PlayerPosition.Value) > movementRadius;
            if (moved)
            {
                _stationaryAnchor = obs.PlayerPosition;
                _stationarySinceUtc = nowUtc;
                _lastMovementUtc = nowUtc;
                _lastMeaningfulGameplayUtc = nowUtc;
            }
            if (_lastMovementUtc.HasValue && (nowUtc - _lastMovementUtc.Value).TotalSeconds < Math.Max(1.0, _config.TravelHoldSeconds))
            {
                _state = CampSocialActivityState.Travel;
                return Build(obs, nowUtc, false, "recent_displacement");
            }

            double stationary = Elapsed(_stationarySinceUtc, nowUtc);
            if (stationary >= Math.Max(_config.SocialDowntimeSeconds, _config.ExtendedDowntimeSeconds))
            {
                _state = CampSocialActivityState.ExtendedDowntime;
                return Build(obs, nowUtc, true, "extended_safe_stationary_downtime");
            }
            if (stationary >= Math.Max(1.0, _config.SocialDowntimeSeconds))
            {
                _state = CampSocialActivityState.SocialDowntime;
                return Build(obs, nowUtc, true, "safe_stationary_downtime");
            }
            _state = CampSocialActivityState.ActiveGameplay;
            return Build(obs, nowUtc, false, "stationary_threshold_pending");
        }

        private SocialActivitySnapshot Build(CampObservation obs, DateTime nowUtc, bool safe, string reason)
        {
            SocialActivitySnapshot value = new SocialActivitySnapshot();
            value.State = _state;
            value.SecondsStationary = Elapsed(_stationarySinceUtc, nowUtc);
            value.SecondsOutOfCombat = Elapsed(_outOfCombatSinceUtc, nowUtc);
            value.SecondsSinceMeaningfulGameplay = Elapsed(_lastMeaningfulGameplayUtc, nowUtc);
            value.Zone = obs == null ? _zone : obs.Zone;
            value.GameplayReady = obs != null && obs.GameplayReady;
            value.SafeForAutoRelax = safe;
            value.Reason = reason;
            return value;
        }

        private void ResetMotion(CampObservation obs, DateTime nowUtc)
        {
            _zone = obs == null ? null : obs.Zone;
            _stationaryAnchor = obs == null ? null : obs.PlayerPosition;
            _stationarySinceUtc = nowUtc;
            _outOfCombatSinceUtc = obs != null && obs.InCombat == false ? (DateTime?)nowUtc : null;
            _lastMeaningfulGameplayUtc = nowUtc;
            _lastMovementUtc = null;
        }

        private static double Elapsed(DateTime? since, DateTime nowUtc)
        {
            return since.HasValue ? Math.Max(0.0, (nowUtc - since.Value).TotalSeconds) : 0.0;
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value == default(DateTime)) return DateTime.UtcNow;
            return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        }
    }
}
