namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// The features an exhibit interface can offer on top of pulling a model apart: seeing through
    /// its outer casing, and swapping between build variants of it.
    ///
    /// This exists to cross an assembly boundary. The model's own driver - <c>Enginei4</c> - lives
    /// in <c>Assembly-CSharp</c>, which already references this assembly, so nothing here can name
    /// that type without closing a reference cycle. An interface declared on this side and
    /// implemented on that one goes the way the dependency already points.
    ///
    /// Implementations are found by casting a serialised <see cref="UnityEngine.MonoBehaviour"/>
    /// reference, because Unity will not serialise a bare interface field.
    /// </summary>
    public interface IExhibitMachinery {
        /// <summary>
        /// How many build variants the model has. Zero means it has none, and the interface should
        /// not offer any.
        /// </summary>
        int VariationCount { get; }

        /// <summary>
        /// Name for the variant at <paramref name="index"/>, short enough for a button.
        /// </summary>
        string GetVariationName(int index);

        /// <summary>
        /// Switches the model to one build variant, showing its parts and hiding every other
        /// variant's.
        ///
        /// Unlike a UI-driven variant switch this must not depend on any interface state of its
        /// own, and must not reactivate anything other than variant parts - an exhibit hides
        /// objects of its own, such as hotspot badges, that a blanket "show everything" pass would
        /// wrongly reveal.
        /// </summary>
        void ApplyVariation(int index);

        /// <summary>
        /// Fades the model's outer casing out, or brings it back.
        /// </summary>
        void SetTransparent(bool transparent);
    }
}
