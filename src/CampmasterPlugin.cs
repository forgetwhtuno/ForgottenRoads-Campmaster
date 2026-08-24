using System;
using System.Collections.Generic;
using System.Globalization;
using Lunaris;
using Lunaris.Config;
using HarmonyLib;
using ForgottenRoads.StandaloneUi;

namespace ErenshorCampmaster
{
    // ---------------------------------------------------------------------
    // Erenshor Campmaster: read-only Hunt Camp observation + deterministic living camp context.
    //
    // This mod observes. It does not assign roles, toggle Auto Pull, pick
    // targets, attack, heal, move Sims, loot, travel, or otherwise play
    // Erenshor. Removing it changes nothing about native behaviour.
    // ---------------------------------------------------------------------
    [LunarisPlugin(PluginGuid, PluginVersion, "forgetwhtuno",
        "Read-only Hunt Camp observation plus deterministic living camp activities/events. Native party AI and gameplay remain authoritative.")]
    [LunarisPermission(LunarisPermission.Reflection | LunarisPermission.Harmony)]
    public sealed class CampmasterPlugin : LunarisPlugin
    {
        internal const string PluginGuid = "forgetwhtuno.erenshor.campmaster";
        internal const string PluginName = "Erenshor Campmaster";
        internal const string PluginVersion = "0.4.0";

        internal static CampmasterPlugin Instance;

        private CampmasterSettings _settings;
        private Harmony _harmony;
        private CampSessionTracker _tracker;
        private RelaxSessionTracker _relaxTracker;
        private CampLivingActivityTracker _livingTracker;
        private SocialActivityTracker _socialActivityTracker;
        private SocialActivitySnapshot _socialActivitySnapshot;
        private float _nextPollSeconds;
        private CampObservation _lastObservation;
        private int _readFailureLogCount;
        private int _pendingControlRelax;
        private CampmasterSuiteAuraProvider _auraProvider;
        private DateTime _nextActivityDiagnosticUtc = DateTime.MinValue;
        private string _lastActivityDiagnosticSignature = string.Empty;

        // Native state is polled at a low fixed rate. Nothing here belongs on
        // a per-frame path.
        private const float PollIntervalSeconds = 1.0f;

        internal CampSessionTracker Tracker { get { return _tracker; } }
        internal RelaxSessionTracker RelaxTracker { get { return _relaxTracker; } }
        internal CampLivingActivityTracker LivingTracker { get { return _livingTracker; } }
        internal CampObservation LastObservation { get { return _lastObservation; } }
        internal SocialActivitySnapshot SocialActivitySnapshot { get { return _socialActivitySnapshot; } }
        internal void RequestRelaxHereFromControl() { _pendingControlRelax = 1; }
        internal void RequestRelaxOffFromControl() { _pendingControlRelax = 2; }

        internal bool TrySetControlSetting(string settingId, string value, out string failure)
        {
            failure = null;
            string normalized;
            if (!CampmasterSuiteDescriptorPolicy.TryNormalizeSettingValue(settingId, value, out normalized))
            {
                failure = string.Equals((settingId ?? string.Empty).Trim(), "autoRecognition", StringComparison.OrdinalIgnoreCase)
                    ? "Expected true or false."
                    : "Unknown setting id.";
                return false;
            }
            bool enabled = string.Equals(normalized, "true", StringComparison.Ordinal);
            if (_tracker == null || _settings == null) { failure = "Campmaster is not ready."; return false; }
            bool oldValue = _tracker.Config.AutoRecognitionEnabled;
            _tracker.SetAutoRecognitionEnabled(enabled);
            _settings.AutoRecognitionEnabled = enabled;
            try { Config.Save(); }
            catch (Exception ex)
            {
                _tracker.SetAutoRecognitionEnabled(oldValue);
                _settings.AutoRecognitionEnabled = oldValue;
                failure = "Could not save Campmaster settings (" + ex.GetType().Name + ").";
                return false;
            }
            return true;
        }

        private void Awake()
        {
            Instance = this;

            _settings = new CampmasterSettings();
            Config.Register(ref _settings);

            CampConfig config = new CampConfig();
            config.AutoRecognitionEnabled = _settings.AutoRecognitionEnabled;
            config.AutoStabilitySeconds = _settings.AutoStabilitySeconds;
            config.DepartureRadius = _settings.DepartureRadius;
            config.DepartureGraceSeconds = _settings.DepartureGraceSeconds;
            config.PartyLossGraceSeconds = _settings.PartyLossGraceSeconds;
            config.SignalLossGraceSeconds = _settings.SignalLossGraceSeconds;
            config.EncounterQuietSeconds = _settings.EncounterQuietSeconds;
            config.RoughEncounterSeconds = _settings.RoughEncounterSeconds;
            config.RepeatedEnemyThreshold = _settings.RepeatedEnemyThreshold;
            _tracker = new CampSessionTracker(config);

            RelaxConfig relaxConfig = new RelaxConfig();
            relaxConfig.DepartureRadius = _settings.RelaxDepartureRadius;
            relaxConfig.DepartureGraceSeconds = _settings.RelaxDepartureGraceSeconds;
            relaxConfig.PartyLossGraceSeconds = _settings.RelaxPartyLossGraceSeconds;
            _relaxTracker = new RelaxSessionTracker(relaxConfig);
            _livingTracker = new CampLivingActivityTracker();
            SocialActivityConfig socialConfig = new SocialActivityConfig();
            socialConfig.SocialDowntimeSeconds = Math.Max(45.0, Math.Min(90.0, _settings.AutoRelaxSeconds));
            socialConfig.ExtendedDowntimeSeconds = Math.Max(socialConfig.SocialDowntimeSeconds, _settings.ExtendedDowntimeSeconds);
            _socialActivityTracker = new SocialActivityTracker(socialConfig);

            _harmony = new Harmony(PluginGuid);
            try
            {
                _harmony.PatchAll();
            }
            catch (Exception ex)
            {
                try { _harmony.UnpatchSelf(); } catch { }
                Logging.LogError("Erenshor Campmaster command hook unavailable (" + ex.GetType().Name + "). Hunt Camp/Relax tracking and the standalone UI will continue; slash-command compatibility is disabled.");
            }

            try
            {
                _auraProvider = new CampmasterSuiteAuraProvider(this);
                _auraProvider.Register();
            }
            catch (Exception ex)
            {
                try { if (_auraProvider != null) _auraProvider.Unregister(); } catch { }
                _auraProvider = null;
                Logging.LogError("Campmaster Suite Aura provider failed to register: " + ex.GetType().Name);
            }

            Logging.LogInfo("Erenshor Campmaster 0.4.0 loaded. Hunt Camp remains read-only; Relax/living-camp activities are deterministic Campmaster context and never drive native actors.");
            StandaloneFallbackUi.Initialize(this, "campmaster", "CAMPMASTER",
                "Declare a camp or start explicit downtime here. Native party/combat state remains authoritative.", 200f, 70f,
                CampmasterControlApi.GetStatus,
                new FallbackAction("Hunt Camp Here", delegate { return TryUiDeclareHuntCamp(); }, null),
                new FallbackAction("Relax Here", delegate { string failure; return CampmasterControlApi.TryRelaxHere(out failure); }, null),
                new FallbackAction("End Hunt Camp", delegate { return TryUiEndHuntCamp(); }, null),
                new FallbackAction("End Relax", delegate { string failure; return CampmasterControlApi.TryRelaxOff(out failure); }, null));
            StandaloneFallbackUi.ConfigureWorkspaceDefaults(225f, 0.97f, 0.93f, 0);
        }

        private void Update()
        {
            StandaloneFallbackUi.Tick(SuiteUiPolicy.IsGameplayReady());
            try
            {
                if (_pendingControlRelax != 0)
                {
                    int request = _pendingControlRelax; _pendingControlRelax = 0;
                    if (request == 1) StartRelax(); else if (request == 2) StopRelax();
                }
                if (_tracker == null) return;
                if (UnityEngine.Time.unscaledTime < _nextPollSeconds) return;
                _nextPollSeconds = UnityEngine.Time.unscaledTime + PollIntervalSeconds;

                DateTime now = DateTime.UtcNow;
                CampObservation obs = NativeGroupStateReader.Read(now);
                obs.GameplayReady = SuiteUiPolicy.IsGameplayReady();
                obs.PvpActive = OptionalCompetitiveStateReader.ReadPvpActive();
                obs.DuelActive = OptionalCompetitiveStateReader.ReadDuelActive();
                _lastObservation = obs;

                if (!obs.ReadSucceeded && _readFailureLogCount < 3)
                {
                    _readFailureLogCount++;
                    Logging.LogWarning("Campmaster could not read native party state; reporting camp facts as unknown.");
                }

                long relaxBefore = _relaxTracker == null ? 0L : _relaxTracker.LatestSequence;
                CampSocialActivityState priorActivity = _socialActivitySnapshot == null
                    ? CampSocialActivityState.ActiveGameplay : _socialActivitySnapshot.State;
                _socialActivitySnapshot = _socialActivityTracker == null ? null : _socialActivityTracker.Tick(obs, now);
                ApplyAutomaticRelax(obs, now);
                if (_relaxTracker != null) _relaxTracker.Tick(obs, now);
                ReportNewRelaxEvents(relaxBefore);
                if (_socialActivitySnapshot != null)
                {
                    string signature = _socialActivitySnapshot.State + "|" + (_socialActivitySnapshot.Reason ?? "unknown");
                    if (priorActivity != _socialActivitySnapshot.State ||
                        !string.Equals(signature, _lastActivityDiagnosticSignature, StringComparison.Ordinal) ||
                        now >= _nextActivityDiagnosticUtc)
                    {
                        _lastActivityDiagnosticSignature = signature;
                        _nextActivityDiagnosticUtc = now.AddSeconds(30.0);
                        Logging.LogInfo("[Campmaster][Activity] state=" + _socialActivitySnapshot.State +
                            " reason=" + (_socialActivitySnapshot.Reason ?? "unknown") +
                            " stationary=" + Math.Round(_socialActivitySnapshot.SecondsStationary) + "s outOfCombat=" +
                            Math.Round(_socialActivitySnapshot.SecondsOutOfCombat) + "s meaningfulAge=" +
                            Math.Round(_socialActivitySnapshot.SecondsSinceMeaningfulGameplay) + "s");
                    }
                }

                // Relax and Hunt Camp are mutually exclusive downtime intents. While Relax is
                // active, do not let the Hunt Camp auto-recognizer create a second session.
                if (_relaxTracker == null || !_relaxTracker.IsActive)
                {
                    long before = _tracker.LatestSequence;
                    _tracker.Tick(obs, now);
                    ReportNewEvents(before);
                }

                long livingBefore = _livingTracker == null ? 0L : _livingTracker.LatestSequence;
                TickLivingActivities(obs, now);
                ReportNewLivingEvents(livingBefore);
            }
            catch (Exception ex)
            {
                Logging.LogError("Campmaster tick failed: " + ex);
            }
        }

        private void ApplyAutomaticRelax(CampObservation obs, DateTime nowUtc)
        {
            if (_relaxTracker == null || _socialActivitySnapshot == null) return;
            bool automaticActive = _relaxTracker.IsActive && _relaxTracker.RecognitionSource == RelaxRecognitionSource.Automatic;
            if (_relaxTracker.IsActive && !automaticActive) return; // explicit player intent always wins

            bool eligible = _settings != null && _settings.AutoRelaxEnabled && _socialActivitySnapshot.SafeForAutoRelax &&
                (_socialActivitySnapshot.State == CampSocialActivityState.SocialDowntime ||
                 _socialActivitySnapshot.State == CampSocialActivityState.ExtendedDowntime);
            if (automaticActive && !eligible)
            {
                _relaxTracker.RequestStop("automatic Relax ended: " + (_socialActivitySnapshot.Reason ?? "activity resumed"));
                return;
            }
            if (!_relaxTracker.IsActive && eligible && obs != null && obs.PlayerPosition.HasValue)
                _relaxTracker.RequestStart(obs.PlayerPosition, RelaxRecognitionSource.Automatic);
        }

        private void OnDestroy()
        {
            StandaloneFallbackUi.Dispose();
            try { if (_livingTracker != null) _livingTracker.Stop(DateTime.UtcNow, "plugin disabled"); } catch { }
            _livingTracker = null;
            try { if (_auraProvider != null) _auraProvider.Unregister(); } catch { }
            _auraProvider = null;
            try { CoopCompatibility.Shutdown(); } catch { }
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch { }
            _harmony = null;
            _pendingControlRelax = 0;
            Instance = null;
        }

        private void TickLivingActivities(CampObservation obs, DateTime nowUtc)
        {
            if (_livingTracker == null) return;
            if (_relaxTracker != null && _relaxTracker.IsActive)
            {
                RelaxSnapshot relax = _relaxTracker.BuildSnapshot(nowUtc);
                if (relax.RecognitionSource == RelaxRecognitionSource.Automatic)
                {
                    // Auto Relax is social context only. It must not create living-camp activities;
                    // those remain explicit Relax or source-proven Hunt Camp behavior.
                    _livingTracker.Tick(CampLivingMode.None, string.Empty, false, obs, nowUtc);
                    return;
                }
                _livingTracker.Tick(CampLivingMode.Relax, relax.SessionId, relax.IsActive, obs, nowUtc);
                return;
            }
            if (_tracker != null && _tracker.IsActive)
            {
                CampSnapshot camp = _tracker.BuildSnapshot(nowUtc);
                _livingTracker.Tick(CampLivingMode.HuntCamp, camp.SessionId, camp.IsActive, obs, nowUtc);
                return;
            }
            _livingTracker.Tick(CampLivingMode.None, string.Empty, false, obs, nowUtc);
        }

        private void ReportNewLivingEvents(long afterSequence)
        {
            if (_livingTracker == null || _livingTracker.LatestSequence <= afterSequence) return;
            List<CampLivingEvent> events = _livingTracker.GetEventsAfter(afterSequence);
            for (int i = 0; i < events.Count; i++)
            {
                CampLivingEvent evt = events[i];
                if (evt == null || !evt.Meaningful) continue;
                OptionalJournalBridge.TryPost(evt);
                string color = evt.PresentationCategory == CampPresentationCategory.Informational ? "lightblue" :
                    evt.PresentationCategory == CampPresentationCategory.Warning ? "yellow" : "grey";
                Logging.LogInfo("[Campmaster][LivingEvent] type=" + evt.Type + " participant=" + (evt.ParticipantName ?? "none") +
                    " counterpart=" + (evt.Counterpart ?? "none") + " subject=" + (evt.SubjectCategory ?? "none") +
                    " subjectSource=" + (evt.SubjectSource ?? "none") + " presentationCategory=" +
                    evt.PresentationCategory.ToString().ToLowerInvariant());
                Chat("[Camp] " + evt.Detail, color);
            }
        }

        private void ReportNewEvents(long afterSequence)
        {
            if (_tracker == null || _tracker.LatestSequence <= afterSequence) return;
            List<CampEvent> events = _tracker.GetEventsAfter(afterSequence);
            for (int i = 0; i < events.Count; i++)
            {
                CampEvent evt = events[i];
                switch (evt.Type)
                {
                    case CampEventType.CampStarted:
                        CampActionPresentationResult started = CampActionPresentation.HuntStart(true, null, Describe(evt.Zone));
                        Chat(started.Text, started.Color);
                        break;
                    case CampEventType.CampEnded:
                        CampActionPresentationResult ended = CampActionPresentation.HuntEnd(true, null, evt.Detail);
                        Chat(ended.Text, ended.Color);
                        break;
                    case CampEventType.CampSuspended:
                        Chat("[Camp] Hunt camp suspended: " + evt.Detail + ".", "yellow");
                        break;
                    case CampEventType.CampResumed:
                        Chat("[Camp] Hunt camp resumed: " + evt.Detail + ".", "lightblue");
                        break;
                }
            }
        }

        private void ReportNewRelaxEvents(long afterSequence)
        {
            if (_relaxTracker == null || _relaxTracker.LatestSequence <= afterSequence) return;
            List<RelaxEvent> events = _relaxTracker.GetEventsAfter(afterSequence);
            for (int i = 0; i < events.Count; i++)
            {
                RelaxEvent evt = events[i];
                switch (evt.Type)
                {
                    case RelaxEventType.RelaxStarted:
                        if (evt.RecognitionSource == RelaxRecognitionSource.Automatic)
                        {
                            Logging.LogInfo("[Campmaster][AutoRelax] state=entered reason=safe_stationary_downtime");
                            break;
                        }
                        Chat("[Relax] Relax started in " + Describe(evt.Zone) + ".", "lightblue");
                        Chat("[Relax] Campmaster will assign visible deterministic camp activities. It still does not move, guard, heal, animate, consume items, or otherwise control the party.", "grey");
                        break;
                    case RelaxEventType.RelaxSuspended:
                        Chat("[Relax] Suspended for combat.", "yellow");
                        break;
                    case RelaxEventType.RelaxResumed:
                        Chat("[Relax] Resumed after combat.", "lightblue");
                        break;
                    case RelaxEventType.RelaxEnded:
                        if (evt.RecognitionSource == RelaxRecognitionSource.Automatic)
                        {
                            Logging.LogInfo("[Campmaster][AutoRelax] state=exited reason=" + (evt.Detail ?? "activity_resumed"));
                            break;
                        }
                        Chat("[Relax] Relax ended: " + evt.Detail + ".", "lightblue");
                        break;
                }
            }
        }

        internal void Chat(string message, string color)
        {
            try { UpdateSocialLog.LogAdd(message, color); }
            catch
            {
                try { UpdateSocialLog.LogAdd(message); }
                catch { }
            }
        }

        private bool TryUiDeclareHuntCamp()
        {
            long before = _tracker == null ? 0 : _tracker.LatestSequence;
            string failure;
            bool success = CampmasterControlApi.TryDeclareHere(out failure);
            ReportNewEvents(before);
            if (!success || _tracker == null || _tracker.LatestSequence <= before)
            {
                CampActionPresentationResult result = CampActionPresentation.HuntStart(success, failure, null);
                Chat(result.Text, result.Color);
            }
            return success;
        }

        private bool TryUiEndHuntCamp()
        {
            long before = _tracker == null ? 0 : _tracker.LatestSequence;
            string failure;
            bool success = CampmasterControlApi.TryClearHuntCamp(out failure);
            ReportNewEvents(before);
            if (!success || _tracker == null || _tracker.LatestSequence <= before)
            {
                CampActionPresentationResult result = CampActionPresentation.HuntEnd(success, failure, success ? "no active context remained" : null);
                Chat(result.Text, result.Color);
            }
            return success;
        }

        internal void LogPatchError(Exception ex)
        {
            try { Logging.LogError("Campmaster command patch failed: " + ex); }
            catch { }
        }

        // -----------------------------------------------------------------
        // Commands
        // -----------------------------------------------------------------
        internal bool TryHandle(TypeText typeText, string raw)
        {
            if (string.IsNullOrEmpty(raw)) return false;
            string command = raw.Trim();
            string argument;

            if (TryMatchCommand(command, "/relax", out argument))
            {
                ClearInput(typeText);
                HandleRelax(argument);
                return true;
            }

            if (!TryMatchCommand(command, "/camp", out argument)) return false;
            ClearInput(typeText);

            string verb = argument == null ? string.Empty : argument.Trim();
            if (verb.Length == 0 || Is(verb, "status")) { PrintStatus(); return true; }
            if (Is(verb, "setup")) { PrintSetupGuide(); return true; }
            if (Is(verb, "here")) { DeclareHere(); return true; }
            if (Is(verb, "clear") || Is(verb, "off")) { ClearCamp(); return true; }
            if (Is(verb, "selftest")) { RunSelfTest(); return true; }

            if (verb.StartsWith("auto", StringComparison.OrdinalIgnoreCase))
            {
                string rest = verb.Substring(4).Trim();
                if (Is(rest, "on")) { SetAuto(true); return true; }
                if (Is(rest, "off")) { SetAuto(false); return true; }
                Chat("[Camp] Automatic recognition is " + (_tracker.Config.AutoRecognitionEnabled ? "ON" : "OFF") +
                     ". Usage: /camp auto on|off", "yellow");
                return true;
            }

            Chat("[Camp] Usage: /camp status | /camp setup | /camp here | /camp clear | /camp auto on|off", "yellow");
            return true;
        }

        private void HandleRelax(string argument)
        {
            string verb = argument == null ? string.Empty : argument.Trim();
            if (verb.Length == 0 || Is(verb, "status")) { PrintRelaxStatus(); return; }
            if (Is(verb, "here") || Is(verb, "on")) { StartRelax(); return; }
            if (Is(verb, "off") || Is(verb, "clear")) { StopRelax(); return; }
            Chat("[Relax] Usage: /relax here | /relax off | /relax status", "yellow");
        }

        internal void StartRelax()
        {
            if (_relaxTracker == null) return;
            if (_tracker != null && _tracker.IsActive)
            {
                Chat("[Relax] A Hunt Camp is active. Use /camp clear before starting Relax.", "yellow");
                return;
            }
            if (_relaxTracker.IsActive)
            {
                if (_relaxTracker.PromoteAutomaticToExplicit())
                {
                    Chat("[Relax] Automatic downtime is now an explicit Relax session.", "lightblue");
                    return;
                }
                Chat("[Relax] Relax is already active.", "yellow");
                return;
            }

            DateTime now = DateTime.UtcNow;
            CampObservation obs = NativeGroupStateReader.Read(now);
            _lastObservation = obs;
            if (obs == null || !obs.ReadSucceeded)
            {
                Chat("[Relax] Cannot read party state right now; try again in a moment.", "yellow");
                return;
            }
            if (!obs.HasParty || obs.LocalResolvedMembers <= 0)
            {
                Chat("[Relax] No locally resolved Sim party detected. Relax is a local party downtime context.", "yellow");
                return;
            }
            if (!obs.PlayerPosition.HasValue)
            {
                Chat("[Relax] Player position is unavailable; cannot establish a Relax anchor.", "yellow");
                return;
            }
            if (!obs.InCombat.HasValue)
            {
                Chat("[Relax] Combat state is unknown; try again when the party is clearly out of combat.", "yellow");
                return;
            }
            if (obs.InCombat.Value)
            {
                Chat("[Relax] Cannot start Relax during combat.", "yellow");
                return;
            }

            _relaxTracker.RequestStart(obs.PlayerPosition);
            long before = _relaxTracker.LatestSequence;
            _relaxTracker.Tick(obs, now);
            ReportNewRelaxEvents(before);
            if (!_relaxTracker.IsActive)
                Chat("[Relax] Relax could not be established from the fresh native party/combat state; try again in a moment.", "yellow");
        }

        internal void StopRelax()
        {
            if (_relaxTracker == null || !_relaxTracker.IsActive)
            {
                Chat("[Relax] Relax is not active.", "yellow");
                return;
            }
            DateTime now = DateTime.UtcNow;
            _relaxTracker.RequestStop();
            long before = _relaxTracker.LatestSequence;
            _relaxTracker.Tick(_lastObservation, now);
            ReportNewRelaxEvents(before);
        }

        private void PrintRelaxStatus()
        {
            if (_relaxTracker == null) return;
            RelaxSnapshot snap = _relaxTracker.BuildSnapshot(DateTime.UtcNow);
            if (!snap.IsActive)
            {
                Chat("RELAX - inactive", "lightblue");
                Chat("  Use /relax here for explicit social downtime. Sitting alone does not declare Relax.", "grey");
                return;
            }

            Chat("RELAX - " + Describe(snap.Zone) + " - " + FormatDuration(snap.ElapsedSeconds), "lightblue");
            Chat("  State: " + snap.State, snap.State == RelaxSessionState.SuspendedForCombat ? "yellow" : "grey");
            Chat("  Recognition: " + (snap.RecognitionSource == RelaxRecognitionSource.Automatic ? "Auto" : "Manual"), "grey");
            if (snap.Party != null && snap.Party.Count > 0)
                Chat("  Party: " + string.Join(", ", snap.Party.ToArray()), "grey");
            Chat("  Authority: " + snap.Authority, "grey");
            if (snap.Anchor.HasValue) Chat("  Anchor: " + snap.Anchor.Value.Describe(), "grey");
            Chat("  Context only: no movement, Guard, combat, healing, or native role settings were changed.", "grey");
        }

        private void PrintSetupGuide()
        {
            if (_relaxTracker != null && _relaxTracker.IsActive)
            {
                Chat("CAMP SETUP - Relax is active", "lightblue");
                Chat("  Next: use /relax off before establishing a Hunt Camp.", "yellow");
                return;
            }

            CampObservation obs = _lastObservation;
            if (obs == null || !obs.ReadSucceeded)
            {
                Chat("CAMP SETUP - native party state unreadable", "lightblue");
                Chat("  Wait a moment and try /camp setup again.", "yellow");
                return;
            }
            if (!obs.HasParty)
            {
                Chat("CAMP SETUP - no party", "lightblue");
                Chat("  Form a normal Sim party first.", "yellow");
                return;
            }

            if (obs.RaidActive == true)
            {
                Chat("CAMP SETUP - raid active", "lightblue");
                Chat("  Hunt Camp recognition is for the normal local Sim party, not raids.", "yellow");
                return;
            }
            if (obs.LocalResolvedMembers <= 0)
            {
                Chat("CAMP SETUP - local Sims unresolved", "lightblue");
                Chat("  Wait for the local Sim party to finish loading and try again.", "yellow");
                return;
            }

            RoleHolder setupMainAssist = obs.DesignatedMainAssist.IsResolved ? obs.DesignatedMainAssist : obs.MainAssist;
            Chat("CAMP SETUP", "lightblue");
            Chat("  Main Tank: " + obs.MainTank.Describe(), obs.MainTank.IsKnown ? "grey" : "yellow");
            Chat("  Main Assist: " + setupMainAssist.Describe(), setupMainAssist.IsKnown ? "grey" : "yellow");
            Chat("  Puller: " + obs.Puller.Describe(), obs.Puller.IsKnown ? "grey" : "yellow");
            if (obs.HealersKnown)
                Chat("  Healing/Mana: " + (obs.HealerNames.Count == 0 ? "none" : string.Join(", ", obs.HealerNames.ToArray())), "grey");
            else Chat("  Healing/Mana: unknown", "grey");
            Chat("  Guard: " + (obs.GuardActive.HasValue ? (obs.GuardActive.Value ? "ON" : "OFF") : "unknown"),
                obs.GuardActive.HasValue && obs.GuardActive.Value ? "grey" : "yellow");
            Chat("  Auto Pull: " + (obs.AutoPullEnabled.HasValue ? (obs.AutoPullEnabled.Value ? "ON" : "OFF") : "unknown"),
                obs.AutoPullEnabled.HasValue && obs.AutoPullEnabled.Value ? "grey" : "yellow");

            if (obs.MainTank.Kind == RoleHolderKind.Unknown)
                Chat("  Next: Main Tank state is unknown; reopen/confirm Manage Roles and recheck.", "yellow");
            else if (obs.MainTank.Kind == RoleHolderKind.None)
                Chat("  Next: assign Main Tank in Erenshor's Manage Roles window.", "yellow");
            else if (setupMainAssist.Kind == RoleHolderKind.Unknown)
                Chat("  Next: Main Assist state is unknown; reopen/confirm Manage Roles and recheck.", "yellow");
            else if (setupMainAssist.Kind == RoleHolderKind.None)
                Chat("  Next: assign Main Assist in Erenshor's Manage Roles window.", "yellow");
            else if (obs.Puller.Kind == RoleHolderKind.Unknown)
                Chat("  Next: Puller state is unknown; reopen/confirm Manage Roles and recheck.", "yellow");
            else if (obs.Puller.Kind == RoleHolderKind.None)
                Chat("  Next: assign a Puller in Erenshor's Manage Roles window.", "yellow");
            else if (!obs.HealersKnown)
                Chat("  Next: Healing/Mana assignment state is unknown; reopen/confirm Manage Roles and recheck.", "yellow");
            else if (obs.HealerNames.Count == 0)
                Chat("  Next: assign Healing/Mana in Erenshor's Manage Roles window for native mana-gated pulling.", "yellow");
            else if (!obs.GuardActive.HasValue)
                Chat("  Next: Guard state is unknown; use native Guard and recheck.", "yellow");
            else if (!obs.GuardActive.Value)
                Chat("  Next: Guard the party first (Shift+6). Guard turns native Auto Pull off.", "yellow");
            else if (!obs.AutoPullEnabled.HasValue)
                Chat("  Next: Auto Pull state is unknown; recheck after using the native control.", "yellow");
            else if (!obs.AutoPullEnabled.Value)
                Chat("  Next: with Guard already set, enable native Auto Pull (Shift+5).", "yellow");
            else
                Chat("  Ready: native Hunt Camp signals are present; hold position through the recognition stability window.", "lightblue");

            if (obs.Puller.Kind == RoleHolderKind.Sim)
                Chat("  Tip: for one-pull testing, target a mob and use native Pull Target (Shift+4); /camp status should show PULL INCOMING when the Sim pull lifecycle is readable.", "grey");
            else if (obs.Puller.Kind == RoleHolderKind.Player)
                Chat("  Tip: Pull Target testing can still use the native control, but Campmaster cannot show PULL INCOMING for a player-held Puller because CurrentPullPhase is Sim-only.", "grey");
        }

        private void DeclareHere()
        {
            if (_tracker == null) return;
            DateTime nowUtc = DateTime.UtcNow;
            CampObservation obs = NativeGroupStateReader.Read(nowUtc);
            _lastObservation = obs;
            if (obs == null || !obs.ReadSucceeded)
            {
                Chat("[Camp] Cannot read party state right now; try again in a moment.", "yellow");
                return;
            }
            if (!obs.HasParty)
            {
                Chat("[Camp] No party detected. /camp here declares a hunting-camp context for your current group.", "yellow");
                return;
            }
            if (obs.LocalResolvedMembers <= 0)
            {
                Chat("[Camp] No locally resolved Sim party is available; wait for the party to finish loading.", "yellow");
                return;
            }
            if (obs.RaidActive == true)
            {
                Chat("[Camp] /camp here is unavailable while a raid is active.", "yellow");
                return;
            }
            if (!obs.PlayerPosition.HasValue)
            {
                Chat("[Camp] Player position is unavailable; cannot establish a safe camp anchor.", "yellow");
                return;
            }

            if (_relaxTracker != null && _relaxTracker.IsActive)
            {
                long relaxBefore = _relaxTracker.LatestSequence;
                _relaxTracker.RequestStop("replaced by /camp here");
                _relaxTracker.Tick(obs, nowUtc);
                ReportNewRelaxEvents(relaxBefore);
            }

            _tracker.RequestDeclareHere(obs.PlayerPosition);
            long before = _tracker.LatestSequence;
            _tracker.Tick(obs, nowUtc);
            ReportNewEvents(before);
            if (_tracker.IsActive)
                Chat("[Camp] Living camp routines are active. Campmaster did not change roles, Auto Pull, targets, movement, inventory, or native stats.", "grey");
            else
                Chat("[Camp] Hunt Camp could not be established from the fresh local party state.", "yellow");
        }

        private void ClearCamp()
        {
            if (_tracker == null) return;
            if (!_tracker.IsActive)
            {
                Chat("[Camp] No hunt camp is active.", "yellow");
                return;
            }
            _tracker.RequestClear();
            long before = _tracker.LatestSequence;
            _tracker.Tick(NativeGroupStateReader.Read(DateTime.UtcNow), DateTime.UtcNow);
            ReportNewEvents(before);
            Chat("[Camp] Cleared Campmaster context. Native pulling was not changed.", "grey");
        }

        private void SetAuto(bool enabled)
        {
            string failure;
            if (!TrySetControlSetting("autoRecognition", enabled ? "true" : "false", out failure))
            {
                Chat("[Camp] Could not change automatic recognition: " + (failure ?? "rejected"), "yellow");
                return;
            }
            Chat("[Camp] Automatic recognition " + (enabled ? "ON" : "OFF") + ".", "lightblue");
        }

        private void RunSelfTest()
        {
            List<string> lines = CampDeterministicTests.Run();
            for (int i = 0; i < lines.Count; i++) Chat("[Camp] " + lines[i], "grey");
        }

        // Only verified facts are printed. Unknown stays unknown.
        private void PrintStatus()
        {
            if (_tracker == null) return;
            CampSnapshot snap = _tracker.BuildSnapshot(DateTime.UtcNow);
            CampObservation obs = _lastObservation;

            if (obs != null && !obs.ReadSucceeded)
                Chat("[Camp] Native party state is currently unreadable; facts below may be missing.", "yellow");

            if (!snap.IsActive)
            {
                Chat("CAMP - none", "lightblue");
                if (_relaxTracker != null && _relaxTracker.IsActive)
                {
                    Chat("  Downtime mode: Relax (use /relax status)", "grey");
                    Chat("  Hunt Camp recognition is suppressed until Relax ends.", "grey");
                    return;
                }
                Chat("  Automatic recognition: " + (_tracker.Config.AutoRecognitionEnabled ? "ON" : "OFF"), "grey");
                if (obs != null && obs.ReadSucceeded && _tracker.Config.AutoRecognitionEnabled)
                    Chat("  Waiting on: " + CampSessionTracker.DescribeRecognitionWait(obs, _tracker.Config.DepartureRadius), "grey");
                Chat("  Use /camp here to declare this spot as your hunting camp.", "grey");
                if (obs != null && obs.ReadSucceeded) PrintNativeFacts(snap);
                else Chat("  Native facts: unavailable on the current sample.", "yellow");
                return;
            }

            string header = "CAMP - " + Describe(snap.Zone) + " - " + FormatDuration(snap.ElapsedSeconds);
            Chat(header, "lightblue");
            Chat("  State: " + snap.State + " (" + (snap.Source == CampRecognitionSource.Auto ? "recognized" : "declared") + ")", "grey");
            Chat("  Activity: " + DescribeActivity(snap), "grey");
            if (snap.Party != null && snap.Party.Count > 0)
                Chat("  Party: " + string.Join(", ", snap.Party.ToArray()), "grey");
            Chat("  Authority: " + snap.Authority, "grey");
            if (snap.Anchor.HasValue)
                Chat("  Anchor: " + snap.Anchor.Value.Describe() + " (" + Describe(snap.AnchorOrigin) + ")", "grey");
            Chat("  Encounters: " + snap.CompletedEncounters.ToString(CultureInfo.InvariantCulture) +
                 " | Verified pulls: " + snap.VerifiedPulls.ToString(CultureInfo.InvariantCulture), "grey");
            if (snap.RoughEncounters > 0 || snap.RepeatedEnemySignals > 0)
                Chat("  Social seeds: rough=" + snap.RoughEncounters.ToString(CultureInfo.InvariantCulture) +
                     ", repeated-target=" + snap.RepeatedEnemySignals.ToString(CultureInfo.InvariantCulture), "grey");
            if (obs != null && obs.ReadSucceeded) PrintNativeFacts(snap);
            else Chat("  Native facts: unavailable on the current sample.", "yellow");
        }

        private void PrintNativeFacts(CampSnapshot snap)
        {
            Chat("  Main Tank: " + snap.MainTank.Describe(), snap.MainTank.Kind == RoleHolderKind.Unknown ? "yellow" : "grey");
            Chat("  Main Assist: " + snap.MainAssist.Describe(), snap.MainAssist.Kind == RoleHolderKind.Unknown ? "yellow" : "grey");
            Chat("  Puller: " + snap.Puller.Describe(), snap.Puller.Kind == RoleHolderKind.Unknown ? "yellow" : "grey");
            if (snap.CrowdControlKnown && snap.CrowdControl.Count > 0)
                Chat("  Crowd Control: " + string.Join(", ", snap.CrowdControl.ToArray()), "grey");
            if (snap.HealersKnown)
                Chat("  Healing/Mana: " + (snap.Healers.Count == 0 ? "none assigned" : string.Join(", ", snap.Healers.ToArray())), "grey");
            else
                Chat("  Healing/Mana: unknown", "grey");

            if (snap.AutoPullEnabled.HasValue)
                Chat("  Auto Pull: " + (snap.AutoPullEnabled.Value ? "ON" : "OFF"), "grey");
            else
                Chat("  Auto Pull: unknown", "grey");

            if (snap.PullerActivelyPulling.HasValue)
                Chat("  Pull lifecycle: " + (snap.PullerActivelyPulling.Value ? "PULL INCOMING" : "idle"), "grey");
            else if (snap.Puller.Kind == RoleHolderKind.Sim)
                Chat("  Pull lifecycle: unknown", "grey");
            else if (snap.Puller.Kind == RoleHolderKind.Player)
                Chat("  Pull lifecycle: unavailable for player Puller (no native Sim CurrentPullPhase)", "grey");
            if (!string.IsNullOrEmpty(snap.CurrentPullTargetName))
                Chat("  Current pull target: " + snap.CurrentPullTargetName, "grey");

            if (snap.HoldingForMana.HasValue)
                Chat("  Native healer mana gate: " + (snap.HoldingForMana.Value ? "HOLDING" : "clear"), "grey");

            if (snap.HoldManaFraction.HasValue)
                Chat("  Hold if healer mana under: " +
                     ((int)Math.Round(snap.HoldManaFraction.Value * 100f)).ToString(CultureInfo.InvariantCulture) + "%", "grey");
            if (snap.MaxPullLevelAbove.HasValue && snap.MaxPullLevelBelow.HasValue)
                Chat("  Auto pull levels: " + snap.MaxPullLevelBelow.Value.ToString(CultureInfo.InvariantCulture) +
                     " to +" + snap.MaxPullLevelAbove.Value.ToString(CultureInfo.InvariantCulture), "grey");
            if (snap.MaxPullDistance.HasValue)
                Chat("  Max pull distance: " + snap.MaxPullDistance.Value.ToString(CultureInfo.InvariantCulture), "grey");
        }

        private static string Describe(string value)
        {
            return string.IsNullOrEmpty(value) ? "unknown" : value;
        }

        private static string DescribeActivity(CampSnapshot snap)
        {
            if (snap == null) return "Unknown";
            if (snap.Activity == CampActivity.Pulling)
                return string.IsNullOrEmpty(snap.CurrentPullTargetName) ? "PULL INCOMING" : "PULL INCOMING - " + snap.CurrentPullTargetName;
            if (snap.Activity == CampActivity.Recovering)
                return "RECOVERING - native healer mana hold";
            if (snap.Activity == CampActivity.Fighting) return "FIGHTING";
            if (snap.Activity == CampActivity.Waiting) return "WAITING";
            return "UNKNOWN";
        }

        private static string FormatDuration(double seconds)
        {
            if (seconds < 60.0) return ((int)seconds).ToString(CultureInfo.InvariantCulture) + "s";
            return ((int)(seconds / 60.0)).ToString(CultureInfo.InvariantCulture) + "m";
        }

        private static bool Is(string value, string expected)
        {
            return string.Equals(value == null ? null : value.Trim(), expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryMatchCommand(string raw, string command, out string argument)
        {
            argument = null;
            if (raw == null || command == null) return false;
            if (!raw.StartsWith(command, StringComparison.OrdinalIgnoreCase)) return false;
            if (raw.Length > command.Length && !char.IsWhiteSpace(raw[command.Length])) return false;
            argument = raw.Length == command.Length ? string.Empty : raw.Substring(command.Length).Trim();
            return true;
        }

        private static void ClearInput(TypeText typeText)
        {
            try
            {
                if (typeText != null && typeText.typed != null) typeText.typed.text = string.Empty;
            }
            catch { }
        }
    }

    // Narrow chat interception. Anything that is not a /camp or /relax command is passed
    // straight through to Erenshor and to any other mod patching this method.
    [HarmonyPatch(typeof(TypeText), "CheckCommands")]
    internal static class CampmasterChatPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(TypeText __instance)
        {
            try
            {
                return CampmasterPlugin.Instance == null ||
                       __instance == null ||
                       __instance.typed == null ||
                       !CampmasterPlugin.Instance.TryHandle(__instance, __instance.typed.text);
            }
            catch (Exception ex)
            {
                if (CampmasterPlugin.Instance != null) CampmasterPlugin.Instance.LogPatchError(ex);
                return true;
            }
        }
    }
}
