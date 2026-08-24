using Lunaris.Config;

namespace ErenshorCampmaster
{
    internal sealed class CampmasterSettings
    {
        public CampmasterSettings() { }

        [Config("AutoRecognition", "Recognition",
            "Automatically recognize a Hunt Camp when the native party is guarding at a common anchor with a verified Puller and Auto Pull enabled. Disable to require /camp here.")]
        public bool AutoRecognitionEnabled = true;

        [Config("StabilitySeconds", "Recognition",
            "Campmaster threshold (not an Erenshor mechanic): how long every required native signal must hold before a camp is recognized.")]
        public double AutoStabilitySeconds = 8.0;

        [Config("DepartureRadius", "Recognition",
            "Campmaster threshold (not an Erenshor mechanic): distance in Unity units from the camp anchor that counts as leaving camp.")]
        public float DepartureRadius = 45f;

        [Config("DepartureGraceSeconds", "Recognition",
            "Campmaster threshold: how long the player must stay beyond DepartureRadius before the camp session ends.")]
        public double DepartureGraceSeconds = 45.0;

        [Config("PartyLossGraceSeconds", "Recognition",
            "Campmaster threshold: grace period for party churn/zoning before a camp session is finalized.")]
        public double PartyLossGraceSeconds = 20.0;

        [Config("SignalLossGraceSeconds", "Recognition",
            "Campmaster threshold: how long unreadable native state may persist before an active camp is suspended.")]
        public double SignalLossGraceSeconds = 20.0;

        [Config("EncounterQuietSeconds", "Session",
            "Campmaster threshold: quiet period after combat clears before a completed encounter is counted.")]
        public double EncounterQuietSeconds = 8.0;

        [Config("RoughEncounterSeconds", "Session",
            "Campmaster-derived classification threshold (not an Erenshor mechanic): combat duration at or above this value emits a rough-encounter seed.")]
        public double RoughEncounterSeconds = 45.0;

        [Config("RepeatedEnemyThreshold", "Session",
            "Campmaster-derived threshold (not an Erenshor mechanic): verified pulls of the same exact target name before one repeated-enemy seed is emitted.")]
        public int RepeatedEnemyThreshold = 3;

        [Config("DepartureRadius", "Relax",
            "Campmaster Relax threshold (not an Erenshor mechanic): distance from the explicit Relax anchor that counts as leaving.")]
        public float RelaxDepartureRadius = 35f;

        [Config("DepartureGraceSeconds", "Relax",
            "How long the player may remain beyond the Relax radius before the Relax session ends.")]
        public double RelaxDepartureGraceSeconds = 15.0;

        [Config("PartyLossGraceSeconds", "Relax",
            "Grace period for temporary party churn before an active Relax session ends.")]
        public double RelaxPartyLossGraceSeconds = 12.0;

        [Config("AutoRelax", "Relax",
            "Automatically expose social Relax context after sustained safe stationary party downtime. This never moves actors or changes gameplay.")]
        public bool AutoRelaxEnabled = true;

        [Config("AutoRelaxSeconds", "Relax",
            "Seconds of verified stationary, ready, out-of-combat party downtime required before automatic Relax context begins.")]
        public double AutoRelaxSeconds = 60.0;

        [Config("ExtendedDowntimeSeconds", "Relax",
            "Seconds of uninterrupted safe stationary downtime before the social activity context becomes ExtendedDowntime.")]
        public double ExtendedDowntimeSeconds = 240.0;
    }
}
