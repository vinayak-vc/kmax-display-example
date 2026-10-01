namespace ViitorCloud.KmaxShowcase {
    /// <summary>
    /// Where a scene is in its lifecycle. See <see cref="ShowcaseScene"/> for why the gap between
    /// <see cref="Engaged"/> and <see cref="Playing"/> is worth keeping.
    /// </summary>
    public enum ShowcaseState {
        /// <summary>Nobody is here. The scene demonstrates itself.</summary>
        Attract,

        /// <summary>Somebody has arrived but has not started. No clock runs.</summary>
        Engaged,

        /// <summary>The task is underway.</summary>
        Playing,

        /// <summary>The task is finished and the result is on screen.</summary>
        Resolved
    }
}
