using System;
using System.Collections.Generic;

namespace ErenshorCampmaster
{
    internal static class SocialActivityDeterministicTests
    {
        internal static List<string> Run()
        {
            List<string> lines = new List<string>();
            DateTime t = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
            SocialActivityConfig cfg = new SocialActivityConfig
            {
                MovementRadius = 3f, TravelHoldSeconds = 6, SocialDowntimeSeconds = 60, ExtendedDowntimeSeconds = 240
            };
            SocialActivityTracker tracker = new SocialActivityTracker(cfg);
            CampObservation obs = Ready(new CampVector3(0, 0, 0));
            tracker.Tick(obs, t);
            Add(lines, "brief stop remains ActiveGameplay", tracker.Tick(obs, t.AddSeconds(30)).State == CampSocialActivityState.ActiveGameplay);
            Add(lines, "safe stationary party enters SocialDowntime", tracker.Tick(obs, t.AddSeconds(61)).State == CampSocialActivityState.SocialDowntime);
            Add(lines, "prolonged downtime enters ExtendedDowntime", tracker.Tick(obs, t.AddSeconds(241)).State == CampSocialActivityState.ExtendedDowntime);
            obs.InCombat = true;
            Add(lines, "recent combat is Combat", tracker.Tick(obs, t.AddSeconds(242)).State == CampSocialActivityState.Combat);
            obs.InCombat = false; obs.PlayerPosition = new CampVector3(10, 0, 0);
            Add(lines, "meaningful displacement is Travel", tracker.Tick(obs, t.AddSeconds(243)).State == CampSocialActivityState.Travel);
            Add(lines, "travel hysteresis prevents immediate flap", tracker.Tick(obs, t.AddSeconds(246)).State == CampSocialActivityState.Travel);
            Add(lines, "travel hold eventually clears", tracker.Tick(obs, t.AddSeconds(250)).State == CampSocialActivityState.ActiveGameplay);
            obs.MeaningfulGameplayActivity = true;
            Add(lines, "meaningful interaction is ActiveGameplay", tracker.Tick(obs, t.AddSeconds(251)).State == CampSocialActivityState.ActiveGameplay);
            obs.MeaningfulGameplayActivity = false; obs.Zone = "New Zone";
            Add(lines, "scene transition invalidates downtime", tracker.Tick(obs, t.AddSeconds(400)).State == CampSocialActivityState.ActiveGameplay);
            obs.GameplayReady = false;
            Add(lines, "non-ready scene cannot enter downtime", !tracker.Tick(obs, t.AddSeconds(500)).SafeForAutoRelax);

            RelaxSessionTracker relax = new RelaxSessionTracker(new RelaxConfig());
            CampObservation safe = Ready(new CampVector3(0, 0, 0));
            relax.RequestStart(safe.PlayerPosition, RelaxRecognitionSource.Automatic); relax.Tick(safe, t);
            Add(lines, "automatic Relax enters as context only", relax.IsActive && relax.RecognitionSource == RelaxRecognitionSource.Automatic);
            Add(lines, "manual intent takes precedence over automatic Relax", relax.PromoteAutomaticToExplicit() && relax.RecognitionSource == RelaxRecognitionSource.Explicit);
            relax.RequestStop("travel resumed"); relax.Tick(safe, t.AddSeconds(1));
            Add(lines, "automatic/explicit tracker exit grants no rewards", !relax.IsActive);
            return lines;
        }

        private static CampObservation Ready(CampVector3 position)
        {
            CampObservation obs = new CampObservation();
            obs.ReadSucceeded = true; obs.GameplayReady = true; obs.PartyPresent = true; obs.LocalResolvedMembers = 1;
            obs.PartyNames.Add("Fiora"); obs.PlayerPosition = position; obs.InCombat = false; obs.PvpActive = false; obs.DuelActive = false;
            obs.Zone = "Brasse"; obs.Authority = CampAuthority.FullLocal;
            return obs;
        }

        private static void Add(List<string> lines, string name, bool pass)
        {
            lines.Add("[SocialActivity] " + name + ": " + (pass ? "PASS" : "FAIL"));
        }
    }
}
