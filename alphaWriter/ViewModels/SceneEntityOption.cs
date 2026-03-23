using CommunityToolkit.Mvvm.ComponentModel;

namespace alphaWriter.ViewModels
{
    /// <summary>
    /// Represents a single character, location, or item in the scene-entity
    /// checklist inside the metadata panel.
    ///
    /// <para><b>IsSelected</b> — the entity is linked to this scene (stored in
    /// Scene.CharacterIds / LocationIds / ItemIds).</para>
    /// <para><b>IsAutoDetected</b> — the entity's name or an alias currently
    /// appears in the scene's text.  This is a display-only, recalculated flag;
    /// it is NOT persisted to the model.</para>
    /// </summary>
    public partial class SceneEntityOption : ObservableObject
    {
        public string Id   { get; }
        public string Name { get; }
        /// <summary>Raw comma-separated aka string, kept for fast re-detection.</summary>
        public string Aka  { get; }

        [ObservableProperty]
        private bool isSelected;

        [ObservableProperty]
        private bool isAutoDetected;

        public SceneEntityOption(string id, string name, string aka,
                                 bool isSelected, bool isAutoDetected)
        {
            Id   = id;
            Name = name;
            Aka  = aka;
            this.isSelected     = isSelected;
            this.isAutoDetected = isAutoDetected;
        }
    }
}
