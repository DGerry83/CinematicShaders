namespace CinematicShaders.UI
{
    public static class CinematicShadersUIStrings
    {
        public static class Common
        {
            public const string Okay = "Okay";
            public const string Cancel = "Cancel";
            public const string WindowTitle = "Cinematic Shaders";
        }

        public static class GTAO
        {
            public const string SectionHeader = "Ambient Occlusion";
            public const string EnableToggle = " Enable Ground-Truth AO";
            public const string EnableTooltip = "Real-time GTAO computation. Disable for normal gameplay, enable for cinematic quality.";
            public const string RawAOToggle = " Show Raw AO Output";
            public const string RawAOTooltip = "Debug view: display grayscale AO without scene composite.";
            public const string DeferredWarning = "GTAO requires deferred rendering.";
            public const string TabName = "GTAO";
        }

        // Future shader tabs:
        // public static class CAS { ... }
    }
}