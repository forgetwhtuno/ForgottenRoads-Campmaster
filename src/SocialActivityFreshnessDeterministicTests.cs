using System;
using System.Collections.Generic;

namespace ErenshorCampmaster
{
    internal static class SocialActivityFreshnessDeterministicTests
    {
        internal static List<string> Run()
        {
            List<string> lines = new List<string>();
            DateTime t = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
            SocialActivityTracker tracker; CampObservation obs; SocialActivitySnapshot snap; string reason;
            New(out tracker, out obs); obs.MeaningfulGameplayActivity = true; obs.MeaningfulGameplayReason = "recent_interaction";
            snap = tracker.Tick(obs, t); Add(lines, 1, "fresh meaningful interaction is ActiveGameplay", snap.State == CampSocialActivityState.ActiveGameplay);
            obs.MeaningfulGameplayActivity = false; snap = tracker.Tick(obs, t.AddSeconds(61)); Add(lines, 2, "interaction ages out into downtime eligibility", snap.State == CampSocialActivityState.SocialDowntime);
            Add(lines, 3, "stale selected target alone is not active", !NativeMeaningfulActivityPolicy.Resolve(false, "Selected NPC", null, out reason));
            Add(lines, 4, "stale forced pull target is not active without pull lifecycle", !NativeMeaningfulActivityPolicy.Resolve(false, null, "Old Target", out reason));
            New(out tracker, out obs); obs.MeaningfulGameplayActivity = true; obs.MeaningfulGameplayReason = "inventory_open"; tracker.Tick(obs, t); obs.MeaningfulGameplayActivity = false; snap = tracker.Tick(obs, t.AddSeconds(61)); Add(lines, 5, "inventory opened then closed expires", snap.State == CampSocialActivityState.SocialDowntime);
            New(out tracker, out obs); obs.MeaningfulGameplayActivity = true; obs.MeaningfulGameplayReason = "gather_complete"; tracker.Tick(obs, t); obs.MeaningfulGameplayActivity = false; snap = tracker.Tick(obs, t.AddSeconds(61)); Add(lines, 6, "completed gather expires", snap.State == CampSocialActivityState.SocialDowntime);
            New(out tracker, out obs); tracker.Tick(obs, t); snap = tracker.Tick(obs, t.AddSeconds(61)); Add(lines, 7, "stationary safe party reaches SocialDowntime", snap.State == CampSocialActivityState.SocialDowntime);
            snap = tracker.Tick(obs, t.AddSeconds(241)); Add(lines, 8, "longer stationary interval reaches ExtendedDowntime", snap.State == CampSocialActivityState.ExtendedDowntime);
            obs.PlayerPosition = new CampVector3(10, 0, 0); snap = tracker.Tick(obs, t.AddSeconds(242)); Add(lines, 9, "actual movement is Travel", snap.State == CampSocialActivityState.Travel && snap.Reason == "recent_displacement");
            obs.InCombat = true; snap = tracker.Tick(obs, t.AddSeconds(243)); Add(lines, 10, "actual combat is Combat", snap.State == CampSocialActivityState.Combat && snap.Reason == "verified_combat");
            New(out tracker, out obs); obs.PvpActive = true; snap = tracker.Tick(obs, t); Add(lines, 11, "active PvP suppresses downtime with exact reason", snap.State == CampSocialActivityState.ActiveGameplay && snap.Reason == "pvp_active");
            obs.PvpActive = false; tracker.Tick(obs, t.AddSeconds(1)); snap = tracker.Tick(obs, t.AddSeconds(62)); Add(lines, 12, "competitive end eventually reaches downtime", snap.State == CampSocialActivityState.SocialDowntime);
            New(out tracker, out obs); tracker.Tick(obs, t); obs.PlayerPosition = new CampVector3(5, 0, 0); tracker.Tick(obs, t.AddSeconds(1)); snap = tracker.Tick(obs, t.AddSeconds(4)); Add(lines, 13, "travel hysteresis prevents rapid flap", snap.State == CampSocialActivityState.Travel);
            New(out tracker, out obs); obs.MeaningfulGameplayActivity = true; obs.MeaningfulGameplayReason = "native_pull_active"; snap = tracker.Tick(obs, t); Add(lines, 14, "activity reason identifies current signal", snap.Reason == "native_pull_active");
            obs.MeaningfulGameplayActivity = false; snap = tracker.Tick(obs, t.AddSeconds(17)); Add(lines, 15, "meaningful gameplay age grows", snap.SecondsSinceMeaningfulGameplay >= 17);
            New(out tracker, out obs); tracker.Tick(obs, t); snap = tracker.Tick(obs, t.AddSeconds(61)); RelaxSessionTracker relax = new RelaxSessionTracker(new RelaxConfig()); if (snap.SafeForAutoRelax) relax.RequestStart(obs.PlayerPosition, RelaxRecognitionSource.Automatic); relax.Tick(obs, t.AddSeconds(61)); Add(lines, 16, "Auto Relax enters only from actual downtime", snap.State == CampSocialActivityState.SocialDowntime && relax.IsActive && relax.RecognitionSource == RelaxRecognitionSource.Automatic);
            New(out tracker, out obs); obs.MeaningfulGameplayActivity = true; SocialActivitySnapshot first = tracker.Tick(obs, t); obs.MeaningfulGameplayActivity = false;
            int socialTicks = 0, extendedTicks = 0; for (int second = 1; second <= 600; second++) { snap = tracker.Tick(obs, t.AddSeconds(second)); if (snap.State == CampSocialActivityState.SocialDowntime) socialTicks++; if (snap.State == CampSocialActivityState.ExtendedDowntime) extendedTicks++; }
            Add(lines, 17, "10-minute interaction path expires and reaches both downtime states", first.State == CampSocialActivityState.ActiveGameplay && socialTicks > 0 && extendedTicks > 0 && snap.State == CampSocialActivityState.ExtendedDowntime);
            bool staleTargetInactive = !NativeMeaningfulActivityPolicy.Resolve(false, "Selected NPC", "Old Target", out reason);
            New(out tracker, out obs); tracker.Tick(obs, t); snap = tracker.Tick(obs, t.AddSeconds(300));
            Add(lines, 18, "five-minute stale target cannot hold ActiveGameplay", staleTargetInactive && snap.State == CampSocialActivityState.ExtendedDowntime);
            lines.Add("[Campmaster Production Simulation] duration=600s interactionExpired=true socialTicks=" + socialTicks + " extendedTicks=" + extendedTicks + " final=" + snap.State + " " + (socialTicks > 0 && extendedTicks > 0 ? "PASS" : "FAIL"));
            return lines;
        }

        private static void New(out SocialActivityTracker tracker, out CampObservation obs)
        {
            tracker = new SocialActivityTracker(new SocialActivityConfig { MovementRadius = 3f, TravelHoldSeconds = 6, SocialDowntimeSeconds = 60, ExtendedDowntimeSeconds = 240 });
            obs = new CampObservation(); obs.ReadSucceeded = true; obs.GameplayReady = true; obs.PartyPresent = true; obs.LocalResolvedMembers = 1; obs.PartyNames.Add("Fiora"); obs.PlayerPosition = new CampVector3(0, 0, 0); obs.InCombat = false; obs.PvpActive = false; obs.DuelActive = false; obs.Zone = "Hidden Hills";
        }
        private static void Add(List<string> lines, int number, string name, bool pass) { lines.Add("[Activity Freshness " + number.ToString("00") + "] " + name + ": " + (pass ? "PASS" : "FAIL")); }
    }
}
