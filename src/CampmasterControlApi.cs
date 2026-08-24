using System;
using ForgottenRoads.StandaloneUi;

namespace ErenshorCampmaster
{
    internal sealed class CampActionPresentationResult
    {
        internal string Text;
        internal string Color;
    }

    internal static class CampActionPresentation
    {
        internal static CampActionPresentationResult HuntStart(bool success, string failure, string zone)
        {
            return new CampActionPresentationResult
            {
                Color = success ? "lightblue" : "yellow",
                Text = success
                    ? "[Camp] Hunt camp started" + (string.IsNullOrWhiteSpace(zone) ? "." : " in " + zone + ".")
                    : "[Camp] Hunt Camp Here failed: " + (string.IsNullOrWhiteSpace(failure) ? "request rejected." : failure)
            };
        }

        internal static CampActionPresentationResult HuntEnd(bool success, string failure, string detail)
        {
            return new CampActionPresentationResult
            {
                Color = success ? "lightblue" : "yellow",
                Text = success
                    ? "[Camp] Hunt camp ended" + (string.IsNullOrWhiteSpace(detail) ? "." : ": " + detail + ".")
                    : "[Camp] End Hunt Camp failed: " + (string.IsNullOrWhiteSpace(failure) ? "request rejected." : failure)
            };
        }
    }

    // Narrow OPTIONAL control surface for explicit player-invoked handoffs
    // from companion mods such as Erenshor Follow / Sim-Led Expeditions.
    //
    // CampmasterApi remains the read-only integration surface. This optional
    // control API may only submit Campmaster's existing context declaration
    // intent against one fresh observation; CampSessionTracker.Tick remains
    // the sole owner of establishing the session. No native roles, Auto Pull,
    // targets, movement, combat,
    // inventory, or saves are changed here.
    public sealed class CampmasterControlState
    {
        public bool Available;
        public bool HuntCampActive;
        public bool RelaxActive;
        public string Mode;
        public string State;
        public string Zone;
    }

    public static class CampmasterControlApi
    {
        public const int SchemaVersion = 1;
        public const int ApiVersion = 1;
        public const string ModuleId = "campmaster";
        public static bool HasDedicatedPanel { get { return true; } }
        public static bool IsPanelOpen { get { return StandaloneFallbackUi.IsOpen; } }

        public static bool IsAvailable
        {
            get
            {
                CampmasterPlugin plugin = CampmasterPlugin.Instance;
                return plugin != null && plugin.Tracker != null;
            }
        }

        public static bool IsHuntCampActive
        {
            get { return CampmasterApi.IsHuntCampActive; }
        }

        public static bool TryDeclareHere(out string failure)
        {
            failure = null;

            CampmasterPlugin plugin = CampmasterPlugin.Instance;
            CampSessionTracker tracker = plugin == null ? null : plugin.Tracker;
            DateTime nowUtc = DateTime.UtcNow;
            CampObservation observation = plugin == null ? null : NativeGroupStateReader.Read(nowUtc);
            CampmasterDeclareHereDecision decision = CampmasterControlPolicy.Evaluate(
                tracker != null,
                tracker != null && tracker.IsActive,
                observation != null,
                observation != null && observation.ReadSucceeded,
                observation != null && observation.HasParty,
                observation != null && observation.LocalResolvedMembers > 0,
                observation != null && observation.RaidActive == true,
                observation != null && observation.PlayerPosition.HasValue);

            if (decision != CampmasterDeclareHereDecision.Accepted)
            {
                failure = CampmasterControlPolicy.FailureMessage(decision);
                return false;
            }

            if (plugin.RelaxTracker != null && plugin.RelaxTracker.IsActive)
            {
                plugin.RelaxTracker.RequestStop("replaced by Hunt Camp control declaration");
                plugin.RelaxTracker.Tick(observation, nowUtc);
                if (plugin.RelaxTracker.IsActive)
                {
                    failure = "Campmaster could not resolve the active Relax session.";
                    return false;
                }
            }

            try
            {
                // Consume the bounded request against the exact same fresh
                // observation that admitted it. There is no deferred request
                // left behind to fire after zoning or a later Follow arrival.
                tracker.RequestDeclareHere(observation.PlayerPosition);
                tracker.Tick(observation, nowUtc);
                if (!tracker.IsActive)
                {
                    failure = "Campmaster could not establish the Hunt Camp from the fresh local party state.";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                failure = "Campmaster rejected the handoff: " + ex.Message;
                return false;
            }
        }

        public static CampmasterControlState GetBasicState()
        {
            var snapshot = CampmasterApi.GetCurrentSnapshot();
            string mode, state, zone;
            snapshot.TryGetValue("mode", out mode);
            snapshot.TryGetValue("state", out state);
            snapshot.TryGetValue("zone", out zone);
            return new CampmasterControlState
            {
                Available = IsAvailable,
                HuntCampActive = CampmasterApi.IsHuntCampActive,
                RelaxActive = CampmasterApi.IsRelaxActive,
                Mode = mode ?? "None",
                State = state ?? "Inactive",
                Zone = zone
            };
        }

        public static string GetStatus()
        {
            CampmasterControlState state = GetBasicState();
            if (!state.Available) return "Campmaster unavailable";
            string text = state.RelaxActive ? ("Relax: " + state.State) : (state.HuntCampActive ? ("Hunt Camp: " + state.State) : "Campmaster idle");
            CampmasterPlugin plugin = CampmasterPlugin.Instance;
            if (plugin == null || plugin.LivingTracker == null || (!state.RelaxActive && !state.HuntCampActive)) return text;
            CampLivingSnapshot living;
            try { living = plugin.LivingTracker.BuildSnapshot(); } catch { return text; }
            text += "\nPreparation: " + living.Preparation.ToString() + "/12 (Campmaster context)";
            if (living.SuspendedForCombat) text += "\nActivities: suspended for combat";
            else if (living.Activities.Count == 0) text += "\nActivities: settling in";
            else
            {
                for (int i = 0; i < living.Activities.Count && i < 5; i++)
                {
                    CampLivingActivity activity = living.Activities[i];
                    if (activity == null) continue;
                    text += "\n" + (activity.ParticipantName ?? ("Sim #" + activity.StableId.ToString())) + " - " + CampLivingActivityTracker.DescribeActivity(activity.Type);
                }
            }
            int shown = 0;
            for (int i = living.RecentEvents.Count - 1; i >= 0 && shown < 2; i--)
            {
                CampLivingEvent evt = living.RecentEvents[i];
                if (evt == null || !evt.Meaningful || string.IsNullOrEmpty(evt.Detail)) continue;
                text += "\nRecent: " + evt.Detail;
                shown++;
            }
            return text;
        }

        public static bool AutoRecognitionEnabled
        {
            get
            {
                CampmasterPlugin plugin = CampmasterPlugin.Instance;
                return plugin != null && plugin.Tracker != null && plugin.Tracker.Config.AutoRecognitionEnabled;
            }
        }

        public static bool TrySetSetting(string settingId, string value, out string failure)
        {
            CampmasterPlugin plugin = CampmasterPlugin.Instance;
            if (plugin == null || plugin.Tracker == null) { failure = "Campmaster is unavailable."; return false; }
            return plugin.TrySetControlSetting(settingId, value, out failure);
        }

        public static bool OpenPanel() { return StandaloneFallbackUi.Open(); }
        public static bool ClosePanel() { return StandaloneFallbackUi.Close(); }

        public static bool TryClearHuntCamp(out string failure)
        {
            failure = null;
            CampmasterPlugin plugin = CampmasterPlugin.Instance;
            CampSessionTracker tracker = plugin == null ? null : plugin.Tracker;
            if (tracker == null) { failure = "Campmaster is unavailable."; return false; }
            if (!tracker.IsActive) return true;
            DateTime nowUtc = DateTime.UtcNow;
            CampObservation observation = NativeGroupStateReader.Read(nowUtc);
            try
            {
                tracker.RequestClear();
                tracker.Tick(observation, nowUtc);
                if (tracker.IsActive) { failure = "Campmaster could not clear the Hunt Camp context."; return false; }
                if (plugin.LivingTracker != null) plugin.LivingTracker.Tick(CampLivingMode.None, string.Empty, false, observation, nowUtc);
                return true;
            }
            catch (Exception ex) { failure = "Campmaster could not clear the Hunt Camp context (" + ex.GetType().Name + ")."; return false; }
        }

        public static bool TryRelaxHere(out string failure)
        {
            failure = null;
            // Outer API-entry safety gate only; StartRelax() (run from the plugin's Update
            // pending-request path) remains the sole owner of the real party/combat/anchor
            // eligibility checks - this does not duplicate or replace that logic.
            if (!SuiteUiPolicy.IsGameplayReady()) { failure = "Gameplay is not ready."; return false; }
            CampmasterPlugin plugin = CampmasterPlugin.Instance;
            if (plugin == null || plugin.RelaxTracker == null) { failure = "Campmaster is unavailable."; return false; }
            plugin.RequestRelaxHereFromControl();
            return true;
        }

        public static bool TryRelaxOff(out string failure)
        {
            failure = null;
            // No readiness gate on the stop path: turning Relax off must always be reachable
            // (e.g. via Hub) even if gameplay readiness is transiently false, so a player is
            // never stuck unable to end an active Relax session.
            CampmasterPlugin plugin = CampmasterPlugin.Instance;
            if (plugin == null || plugin.RelaxTracker == null) { failure = "Campmaster is unavailable."; return false; }
            if (!plugin.RelaxTracker.IsActive) return true;
            plugin.RequestRelaxOffFromControl();
            return true;
        }
    }
}
