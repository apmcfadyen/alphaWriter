using alphaWriter.Models.Analysis;

namespace alphaWriter.ViewModels
{
    /// <summary>A heading ("Chapter › Scene") and its notes, for the grouped Revise list.</summary>
    public class NoteGroup : List<NlpNote>
    {
        public NoteGroup(string title, string sceneId, IEnumerable<NlpNote> notes) : base(notes)
        {
            Title = title;
            SceneId = sceneId;
        }

        public string Title { get; }

        /// <summary>Empty for book- and chapter-level notes.</summary>
        public string SceneId { get; }

        public string Header => $"{Title}  ({Count})";
    }
}
