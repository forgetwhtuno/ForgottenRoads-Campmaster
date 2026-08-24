using System;

namespace ErenshorCampmaster
{
    internal static class CampmasterControlPolicyTests
    {
        private static int _failures;

        private static int Main()
        {
            Check("ready + readable party accepts policy", Evaluate(true, false, true, true, true, true, false, true) == CampmasterDeclareHereDecision.Accepted);
            Check("missing tracker rejects request", Evaluate(false, false, true, true, true, true, false, true) == CampmasterDeclareHereDecision.NotReady);
            Check("active camp rejects duplicate request", Evaluate(true, true, true, true, true, true, false, true) == CampmasterDeclareHereDecision.AlreadyActive);
            Check("missing observation rejects request", Evaluate(true, false, false, false, false, false, false, false) == CampmasterDeclareHereDecision.ObservationUnavailable);
            Check("failed native read rejects request", Evaluate(true, false, true, false, true, true, false, true) == CampmasterDeclareHereDecision.ObservationUnavailable);
            Check("no party rejects request", Evaluate(true, false, true, true, false, false, false, true) == CampmasterDeclareHereDecision.NoParty);
            Check("no local Sims rejects request", Evaluate(true, false, true, true, true, false, false, true) == CampmasterDeclareHereDecision.NoLocalParty);
            Check("raid rejects request", Evaluate(true, false, true, true, true, true, true, true) == CampmasterDeclareHereDecision.RaidActive);
            Check("missing anchor rejects request", Evaluate(true, false, true, true, true, true, false, false) == CampmasterDeclareHereDecision.MissingAnchor);
            Check("rejections provide a reason", RejectionsHaveReasons());
            Check("control API absent plugin is unavailable", ApiAbsentPluginIsUnavailable());
            Check("control API consumes accepted request immediately", ApiQueuesAcceptedRequest());
            Check("accepted Hunt Camp handoff replaces Relax", ApiReplacesRelax());
            Check("rejected Hunt Camp handoff preserves Relax", ApiRejectedRequestKeepsRelax());
            Check("control API rejects active camp", ApiRejectsActiveCamp());
            Check("control API rejects unreadable observation", ApiRejectsUnreadableObservation());
            Check("control API rejects missing party", ApiRejectsMissingParty());
            Check("Suite basic descriptor uses exact bool wire", DescriptorUsesExactBoolWire());
            Check("Suite auto-recognition setting routes", DescriptorSettingRoutes());
            Check("Suite invalid setting values reject", DescriptorSettingRejectsInvalid());
            Check("control API mutates only Campmaster recognition", ApiSetsAutoRecognition());
            Check("Hunt Camp UI success presentation is visible", CampActionPresentation.HuntStart(true, null, "Brasse").Text.Contains("started in Brasse"));
            Check("Hunt Camp UI failure presentation is visible", CampActionPresentation.HuntStart(false, "No party is currently detected.", null).Text.Contains("No party"));
            Check("End Hunt Camp presentation shares event wording", CampActionPresentation.HuntEnd(true, null, "cleared by player").Text.Contains("ended: cleared by player"));
            Check("Suite status is bounded", DescriptorStatusBounded());
            Check("Suite descriptor excludes sensitive fields", DescriptorPrivacySafe());
            Check("Suite advertises fallback panel and only existing Relax actions", DescriptorActionsAreRelaxOnly());

            Console.WriteLine(_failures == 0
                ? "Campmaster control API deterministic tests: ALL PASS"
                : "Campmaster control API deterministic tests: " + _failures + " FAIL");
            return _failures == 0 ? 0 : 1;
        }

        private static CampmasterDeclareHereDecision Evaluate(bool ready, bool active, bool present, bool readable, bool party,
            bool localParty, bool raid, bool anchor)
        {
            return CampmasterControlPolicy.Evaluate(ready, active, present, readable, party, localParty, raid, anchor);
        }

        private static bool RejectionsHaveReasons()
        {
            CampmasterDeclareHereDecision[] rejected =
            {
                CampmasterDeclareHereDecision.NotReady,
                CampmasterDeclareHereDecision.AlreadyActive,
                CampmasterDeclareHereDecision.ObservationUnavailable,
                CampmasterDeclareHereDecision.NoParty,
                CampmasterDeclareHereDecision.NoLocalParty,
                CampmasterDeclareHereDecision.RaidActive,
                CampmasterDeclareHereDecision.MissingAnchor
            };
            for (int i = 0; i < rejected.Length; i++)
                if (string.IsNullOrWhiteSpace(CampmasterControlPolicy.FailureMessage(rejected[i]))) return false;
            return CampmasterControlPolicy.FailureMessage(CampmasterDeclareHereDecision.Accepted) == null;
        }

        private static bool ApiAbsentPluginIsUnavailable()
        {
            CampmasterPlugin.Instance = null;
            return !CampmasterControlApi.IsAvailable;
        }

        private static bool ApiQueuesAcceptedRequest()
        {
            CampSessionTracker tracker = NewTracker(false);
            CampmasterPlugin.Instance = new CampmasterPlugin { Tracker = tracker };
            NativeGroupStateReader.Observation = FreshObservation(true, true, false, true);
            string failure;
            bool accepted = CampmasterControlApi.TryDeclareHere(out failure);
            return accepted && failure == null && tracker.RequestCount == 1 && tracker.TickCount == 1 &&
                   tracker.LastRequestedPosition.HasValue && tracker.LastRequestedPosition.Value.X == 5f && tracker.IsActive;
        }

        private static bool ApiReplacesRelax()
        {
            CampSessionTracker tracker = NewTracker(false);
            RelaxSessionTracker relax = new RelaxSessionTracker { IsActive = true };
            CampmasterPlugin.Instance = new CampmasterPlugin { Tracker = tracker, RelaxTracker = relax };
            NativeGroupStateReader.Observation = FreshObservation(true, true, false, true);
            string failure;
            bool accepted = CampmasterControlApi.TryDeclareHere(out failure);
            return accepted && failure == null && tracker.IsActive && !relax.IsActive && relax.StopCount == 1;
        }

        private static bool ApiRejectedRequestKeepsRelax()
        {
            CampSessionTracker tracker = NewTracker(false);
            RelaxSessionTracker relax = new RelaxSessionTracker { IsActive = true };
            CampmasterPlugin.Instance = new CampmasterPlugin { Tracker = tracker, RelaxTracker = relax };
            NativeGroupStateReader.Observation = FreshObservation(false, false, false, true);
            string failure;
            bool accepted = CampmasterControlApi.TryDeclareHere(out failure);
            return !accepted && !string.IsNullOrWhiteSpace(failure) && relax.IsActive && relax.StopCount == 0 && !tracker.IsActive;
        }

        private static bool ApiRejectsActiveCamp()
        {
            CampSessionTracker tracker = NewTracker(true);
            CampmasterPlugin.Instance = new CampmasterPlugin { Tracker = tracker };
            NativeGroupStateReader.Observation = FreshObservation(true, true, false, true);
            string failure;
            return !CampmasterControlApi.TryDeclareHere(out failure) && tracker.RequestCount == 0 &&
                   failure == "A hunt camp is already active.";
        }

        private static bool ApiRejectsUnreadableObservation()
        {
            CampSessionTracker tracker = NewTracker(false);
            CampmasterPlugin.Instance = new CampmasterPlugin { Tracker = tracker };
            NativeGroupStateReader.Observation = FreshObservation(true, true, false, true);
            NativeGroupStateReader.Observation.ReadSucceeded = false;
            string failure;
            return !CampmasterControlApi.TryDeclareHere(out failure) && tracker.RequestCount == 0 &&
                   !string.IsNullOrWhiteSpace(failure);
        }

        private static bool ApiRejectsMissingParty()
        {
            CampSessionTracker tracker = NewTracker(false);
            CampmasterPlugin.Instance = new CampmasterPlugin { Tracker = tracker };
            NativeGroupStateReader.Observation = FreshObservation(false, false, false, true);
            string failure;
            return !CampmasterControlApi.TryDeclareHere(out failure) && tracker.RequestCount == 0 &&
                   failure == "No party is currently detected.";
        }

        private static bool DescriptorUsesExactBoolWire()
        {
            string on = CampmasterSuiteDescriptorPolicy.BuildBasicSettings(true);
            string off = CampmasterSuiteDescriptorPolicy.BuildBasicSettings(false);
            return Field(on, "type") == "bool" && Field(on, "value") == "true" &&
                   Field(off, "value") == "false" && Field(on, "tier") == "basic";
        }

        private static bool DescriptorSettingRoutes()
        {
            string normalized;
            return CampmasterSuiteDescriptorPolicy.TryNormalizeSettingValue("autoRecognition", "TRUE", out normalized) && normalized == "true" &&
                   CampmasterSuiteDescriptorPolicy.TryNormalizeSettingValue("autoRecognition", "false", out normalized) && normalized == "false";
        }

        private static bool DescriptorSettingRejectsInvalid()
        {
            string normalized;
            return !CampmasterSuiteDescriptorPolicy.TryNormalizeSettingValue("autoRecognition", "1", out normalized) &&
                   !CampmasterSuiteDescriptorPolicy.TryNormalizeSettingValue("autoRecognition", "on", out normalized) &&
                   !CampmasterSuiteDescriptorPolicy.TryNormalizeSettingValue("autoPull", "true", out normalized);
        }

        private static bool ApiSetsAutoRecognition()
        {
            CampSessionTracker tracker = NewTracker(false);
            tracker.SetAutoRecognitionEnabled(true);
            CampmasterPlugin.Instance = new CampmasterPlugin { Tracker = tracker };
            string failure;
            bool ok = CampmasterControlApi.TrySetSetting("autoRecognition", "false", out failure);
            return ok && failure == null && !tracker.Config.AutoRecognitionEnabled;
        }

        private static bool DescriptorStatusBounded()
        {
            string payload = CampmasterSuiteDescriptorPolicy.BuildDescribe("0.4.0", new string('s', 500));
            return Field(payload, "status").Length == CampmasterSuiteDescriptorPolicy.MaxHubText;
        }

        private static bool DescriptorPrivacySafe()
        {
            return !CampmasterSuiteDescriptorPolicy.ContainsSensitiveFieldName(
                CampmasterSuiteDescriptorPolicy.BuildDescribe("0.4.0", "Campmaster idle")) &&
                   !CampmasterSuiteDescriptorPolicy.ContainsSensitiveFieldName(
                CampmasterSuiteDescriptorPolicy.BuildBasicSettings(true));
        }

        private static bool DescriptorActionsAreRelaxOnly()
        {
            string payload = CampmasterSuiteDescriptorPolicy.BuildDescribe("0.4.0", "Campmaster idle");
            return Field(payload, "actions") == "openPanel,closePanel,relaxHere,relaxOff";
        }

        private static string Field(string line, string key)
        {
            string[] pairs = (line ?? string.Empty).Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                int eq = pairs[i].IndexOf('=');
                if (eq <= 0) continue;
                string k = Uri.UnescapeDataString(pairs[i].Substring(0, eq));
                if (k == key) return Uri.UnescapeDataString(pairs[i].Substring(eq + 1));
            }
            return string.Empty;
        }

        private static CampSessionTracker NewTracker(bool active)
        {
            CampSessionTracker tracker = new CampSessionTracker();
            tracker.IsActive = active;
            return tracker;
        }

        private static CampObservation FreshObservation(bool party, bool localParty, bool raid, bool anchor)
        {
            return new CampObservation
            {
                ReadSucceeded = true,
                HasParty = party,
                LocalResolvedMembers = localParty ? 1 : 0,
                RaidActive = raid,
                PlayerPosition = anchor ? (CampVector3?)new CampVector3(5f, 0f, 7f) : null
            };
        }

        private static void Check(string name, bool passed)
        {
            Console.WriteLine((passed ? "PASS  " : "FAIL  ") + name);
            if (!passed) _failures++;
        }
    }

    // Minimal stubs let the standalone harness compile the production
    // CampmasterControlApi itself without Unity/BepInEx/game assemblies.
    internal sealed class CampmasterPlugin
    {
        internal static CampmasterPlugin Instance;
        internal CampSessionTracker Tracker { get; set; }
        internal CampLivingActivityTracker LivingTracker { get; set; }
        internal CampObservation LastObservation { get; set; }
        internal RelaxSessionTracker RelaxTracker { get; set; }
        internal void RequestRelaxHereFromControl() { if (RelaxTracker != null) RelaxTracker.RequestStart(); }
        internal void RequestRelaxOffFromControl() { if (RelaxTracker != null) RelaxTracker.RequestStop("control"); }
        internal bool TrySetControlSetting(string settingId, string value, out string failure)
        {
            string normalized;
            if (Tracker == null || !CampmasterSuiteDescriptorPolicy.TryNormalizeSettingValue(settingId, value, out normalized))
            {
                failure = "rejected";
                return false;
            }
            Tracker.SetAutoRecognitionEnabled(normalized == "true");
            failure = null;
            return true;
        }
    }

    // Test-only stand-in for the real Unity-backed SuiteUiPolicy gate (src/SuiteUiPolicy.cs).
    // This harness has no Unity/game assemblies to evaluate real readiness against, so it
    // always reports ready; readiness gating itself remains NEEDS LIVE TEST.
    internal static class SuiteUiPolicy
    {
        internal static bool IsGameplayReady() { return true; }
    }

    internal sealed class RelaxSessionTracker
    {
        internal bool IsActive { get; set; }
        internal int StopCount { get; private set; }
        internal int StartCount { get; private set; }
        private bool _pendingStop;
        private bool _pendingStart;

        internal void RequestStart()
        {
            StartCount++;
            _pendingStart = true;
        }

        internal void RequestStop(string reason)
        {
            StopCount++;
            _pendingStop = true;
        }

        internal void Tick(CampObservation observation, DateTime nowUtc)
        {
            if (_pendingStop)
            {
                _pendingStop = false;
                IsActive = false;
            }
            if (_pendingStart)
            {
                _pendingStart = false;
                IsActive = true;
            }
        }
    }

    internal sealed class CampSessionTracker
    {
        internal bool IsActive { get; set; }
        internal CampConfig Config { get; private set; }
        internal CampSessionTracker() { Config = new CampConfig(); }
        internal void SetAutoRecognitionEnabled(bool enabled) { Config.AutoRecognitionEnabled = enabled; }
        internal int RequestCount { get; private set; }
        internal int TickCount { get; private set; }
        internal CampVector3? LastRequestedPosition { get; private set; }

        internal void RequestDeclareHere(CampVector3? playerPosition)
        {
            RequestCount++;
            LastRequestedPosition = playerPosition;
        }

        internal void RequestClear()
        {
            IsActive = false;
        }

        internal void Tick(CampObservation observation, DateTime nowUtc)
        {
            TickCount++;
            if (observation != null && observation.ReadSucceeded && observation.HasParty &&
                observation.LocalResolvedMembers > 0 && observation.RaidActive != true &&
                LastRequestedPosition.HasValue) IsActive = true;
        }
    }

    internal sealed class CampObservation
    {
        internal bool ReadSucceeded;
        internal bool HasParty;
        internal int LocalResolvedMembers;
        internal bool? RaidActive;
        internal CampVector3? PlayerPosition;
        internal bool InCombat;
        internal bool PullerActivelyPulling;
        internal System.Collections.Generic.List<CampParticipantObservation> Participants =
            new System.Collections.Generic.List<CampParticipantObservation>();
    }

    internal sealed class CampParticipantObservation
    {
        internal int StableId = -1;
        internal string Name;
        internal bool LocalUsable;
        internal bool Remote;
        internal bool KnownDead;
    }

    internal struct CampVector3
    {
        internal readonly float X;
        internal readonly float Y;
        internal readonly float Z;

        internal CampVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    internal sealed class CampConfig
    {
        internal bool AutoRecognitionEnabled = true;
    }

    internal static class NativeGroupStateReader
    {
        internal static CampObservation Observation;
        internal static CampObservation Read(DateTime nowUtc) { return Observation; }
    }

    public static class CampmasterApi
    {
        public static bool IsHuntCampActive
        {
            get
            {
                return CampmasterPlugin.Instance != null && CampmasterPlugin.Instance.Tracker != null &&
                       CampmasterPlugin.Instance.Tracker.IsActive;
            }
        }

        public static bool IsRelaxActive
        {
            get
            {
                return CampmasterPlugin.Instance != null && CampmasterPlugin.Instance.RelaxTracker != null &&
                       CampmasterPlugin.Instance.RelaxTracker.IsActive;
            }
        }

        public static System.Collections.Generic.Dictionary<string, string> GetCurrentSnapshot()
        {
            return new System.Collections.Generic.Dictionary<string, string>();
        }
    }
}
