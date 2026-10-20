using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using alphaWriter.Models;
using alphaWriter.Models.Analysis;

namespace alphaWriter.Services.Nlp
{
    public interface INlpAnalysisService
    {
        Task<SceneAnalysisResult> AnalyzeSceneAsync(Scene scene, Chapter chapter, Book book,
            CancellationToken ct = default);

        Task<List<SceneAnalysisResult>> AnalyzeChapterAsync(Chapter chapter, Book book,
            CancellationToken ct = default);

        Task<(List<NlpNote> Notes, List<SceneAnalysisResult> Results, List<CharacterVoiceProfile> VoiceProfiles, List<DialogueVoiceProfile> DialogueProfiles)> AnalyzeBookAsync(Book book,
            IProgress<string>? progress = null, CancellationToken ct = default,
            IReadOnlyList<SceneAnalysisResult>? previousResults = null);

        /// <summary>
        /// Fast single-scene pass for live feedback while editing: lexical rules always,
        /// POS rules only if the tagger happens to be loaded. No models are loaded.
        /// </summary>
        LiveSceneAnalysis AnalyzeSceneLive(Scene scene, Chapter chapter, Book book);
    }
}
