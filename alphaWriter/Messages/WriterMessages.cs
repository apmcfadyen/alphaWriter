using CommunityToolkit.Mvvm.Messaging.Messages;

namespace alphaWriter.Messages
{
    /// <summary>Sent when entity names/aliases change so the editor can re-highlight.</summary>
    public sealed class EntitiesChangedMessage : ValueChangedMessage<bool>
    {
        public EntitiesChangedMessage() : base(true) { }
    }

    /// <summary>Sent when analysis completes or is loaded from cache, so the bottom panel can refresh.</summary>
    public sealed class AnalysisResultsAvailableMessage : ValueChangedMessage<bool>
    {
        public AnalysisResultsAvailableMessage() : base(true) { }
    }

    /// <summary>Sent when the reports panel should load/refresh its WebView content.</summary>
    public sealed class RequestReportRefreshMessage : ValueChangedMessage<string>
    {
        public RequestReportRefreshMessage(string reportType) : base(reportType) { }
    }

    /// <summary>Sent to navigate the editor to a specific scene and optionally a sentence.</summary>
    public sealed class NavigateToSentenceMessage
    {
        public string SceneId { get; }
        public int? SentenceIndex { get; }

        public NavigateToSentenceMessage(string sceneId, int? sentenceIndex = null)
        {
            SceneId = sceneId;
            SentenceIndex = sentenceIndex;
        }
    }

    /// <summary>Sent when a right-panel content request is made (book info, element editor).</summary>
    public sealed class RightPanelRequestMessage
    {
        public RightPanelMode Mode { get; }
        public RightPanelRequestMessage(RightPanelMode mode) => Mode = mode;
    }

    public enum RightPanelMode
    {
        None,
        SceneMetadata,
        BookInfo,
        ElementEditor
    }
}
