using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using alphaWriter.Models.Analysis;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace alphaWriter.Services.Nlp
{
    public class EmotionService : IEmotionService
    {
        private readonly INlpModelManager _modelManager;
        private InferenceSession? _session;
        private CodeGenTokenizer? _tokenizer;
        private const int MaxTokenLength = 128;
        private const float ConfidenceThreshold = 0.3f;
        private const int NumLabels = 28; // GoEmotions: 28 classes (Admiration..Neutral)
        private const int BatchSize = 32; // Max texts per ONNX forward pass

        // Track which ONNX input names the model expects
        private bool _hasTokenTypeIds;

        public EmotionService(INlpModelManager modelManager)
        {
            _modelManager = modelManager;
        }

        public bool IsLoaded => _session is not null && _tokenizer is not null;

        public Task LoadModelAsync(CancellationToken ct = default)
        {
            if (IsLoaded) return Task.CompletedTask;

            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                var vocabPath = _modelManager.GetTokenizerPath(NlpModelManager.EmotionModelName);
                var mergesPath = _modelManager.GetMergesPath(NlpModelManager.EmotionModelName);

                // RoBERTa uses GPT-2 style BPE tokenizer; CodeGenTokenizer handles this.
                // Constructors are private — use the static Create factory method.
                using var vocabStream = File.OpenRead(vocabPath);
                using var mergesStream = File.OpenRead(mergesPath);
                _tokenizer = CodeGenTokenizer.Create(
                    vocabStream, mergesStream,
                    addPrefixSpace: true,
                    addBeginOfSentence: false,  // We add BOS/EOS manually for RoBERTa
                    addEndOfSentence: false);

                ct.ThrowIfCancellationRequested();

                var modelPath = _modelManager.GetModelPath(NlpModelManager.EmotionModelName);
                var options = new SessionOptions();
                options.InterOpNumThreads = 1;
                options.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2);
                _session = new InferenceSession(modelPath, options);

                // Check if model expects token_type_ids (RoBERTa typically doesn't)
                _hasTokenTypeIds = _session.InputMetadata.ContainsKey("token_type_ids");
            }, ct);
        }

        public void UnloadModel()
        {
            _session?.Dispose();
            _session = null;
            _tokenizer = null;
        }

        public List<(EmotionLabel Label, float Confidence)> ClassifySentence(string text)
        {
            if (!IsLoaded)
                throw new InvalidOperationException("Model not loaded. Call LoadModelAsync first.");

            var encoded = EncodeText(text);
            var logits = RunInference(encoded.inputIds, encoded.attentionMask, 1, encoded.length);

            return ExtractEmotions(logits, 0);
        }

        public List<List<(EmotionLabel Label, float Confidence)>> ClassifyBatch(IReadOnlyList<string> texts)
        {
            if (!IsLoaded)
                throw new InvalidOperationException("Model not loaded. Call LoadModelAsync first.");

            if (texts.Count == 0) return [];

            var allResults = new List<List<(EmotionLabel Label, float Confidence)>>(texts.Count);

            for (int batchStart = 0; batchStart < texts.Count; batchStart += BatchSize)
            {
                int batchEnd = Math.Min(batchStart + BatchSize, texts.Count);
                int currentBatch = batchEnd - batchStart;

                // Encode all texts in this batch
                var encodings = new (long[] inputIds, long[] attentionMask, int length)[currentBatch];
                int maxLen = 0;
                for (int i = 0; i < currentBatch; i++)
                {
                    encodings[i] = EncodeText(texts[batchStart + i]);
                    maxLen = Math.Max(maxLen, encodings[i].length);
                }

                // Build padded tensors [currentBatch, maxLen]
                var batchInputIds = new long[currentBatch * maxLen];
                var batchAttention = new long[currentBatch * maxLen];

                for (int i = 0; i < currentBatch; i++)
                {
                    var (ids, mask, len) = encodings[i];
                    int offset = i * maxLen;
                    Array.Copy(ids, 0, batchInputIds, offset, len);
                    Array.Copy(mask, 0, batchAttention, offset, len);
                    // Remaining positions are already 0 (padding)
                }

                // Single ONNX forward pass — output: [currentBatch, NumLabels]
                var allLogits = RunInference(batchInputIds, batchAttention, currentBatch, maxLen);

                // Extract per-sequence emotion results
                for (int i = 0; i < currentBatch; i++)
                {
                    allResults.Add(ExtractEmotions(allLogits, i));
                }
            }

            return allResults;
        }

        private List<(EmotionLabel Label, float Confidence)> ExtractEmotions(float[] logits, int sequenceIndex)
        {
            var results = new List<(EmotionLabel Label, float Confidence)>();
            int offset = sequenceIndex * NumLabels;
            int labelCount = Math.Min(logits.Length - offset, NumLabels);

            for (int i = 0; i < labelCount; i++)
            {
                float confidence = Sigmoid(logits[offset + i]);
                if (confidence >= ConfidenceThreshold)
                {
                    results.Add(((EmotionLabel)i, confidence));
                }
            }

            // Sort by confidence descending
            results.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
            return results;
        }

        private (long[] inputIds, long[] attentionMask, int length) EncodeText(string text)
        {
            // Encode with truncation, reserving 2 tokens for <s> and </s>
            string? normalizedText;
            int charsConsumed;
            var ids = _tokenizer!.EncodeToIds(text, MaxTokenLength - 2,
                addPrefixSpace: true,
                addBeginningOfSentence: false,
                addEndOfSentence: false,
                normalizedText: out normalizedText,
                charsConsumed: out charsConsumed,
                considerPreTokenization: true,
                considerNormalization: true);

            // Build <s> ids </s> sequence (RoBERTa special tokens)
            int bosId = _tokenizer.BeginningOfSentenceId ?? 0;  // <s> = 0
            int eosId = _tokenizer.EndOfSentenceId ?? 2;        // </s> = 2

            int seqLen = ids.Count + 2;
            var inputIds = new long[seqLen];
            var attentionMask = new long[seqLen];

            inputIds[0] = bosId;
            attentionMask[0] = 1;

            for (int i = 0; i < ids.Count; i++)
            {
                inputIds[i + 1] = ids[i];
                attentionMask[i + 1] = 1;
            }

            inputIds[seqLen - 1] = eosId;
            attentionMask[seqLen - 1] = 1;

            return (inputIds, attentionMask, seqLen);
        }

        private float[] RunInference(long[] inputIds, long[] attentionMask,
            int batchSize, int seqLength)
        {
            var shape = new[] { batchSize, seqLength };

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids",
                    new DenseTensor<long>(inputIds, shape)),
                NamedOnnxValue.CreateFromTensor("attention_mask",
                    new DenseTensor<long>(attentionMask, shape))
            };

            // Only add token_type_ids if the model expects it
            if (_hasTokenTypeIds)
            {
                var tokenTypeIds = new long[batchSize * seqLength]; // all zeros
                inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids",
                    new DenseTensor<long>(tokenTypeIds, shape)));
            }

            using var results = _session!.Run(inputs);

            // Output: logits [batchSize, 28]
            var output = results.First();
            var value = output.Value;

            if (value is DenseTensor<float> denseTensor)
                return denseTensor.Buffer.Span.ToArray();

            // Fallback
            var tensor = output.AsTensor<float>();
            var data = new float[tensor.Length];
            int idx = 0;
            foreach (var val in tensor)
                data[idx++] = val;
            return data;
        }

        private static float Sigmoid(float x)
        {
            return 1.0f / (1.0f + MathF.Exp(-x));
        }

        public void Dispose()
        {
            _session?.Dispose();
            _session = null;
        }
    }
}
