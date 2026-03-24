using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics.Tensors;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace alphaWriter.Services.Nlp
{
    public class EmbeddingService : IEmbeddingService
    {
        private readonly INlpModelManager _modelManager;
        private InferenceSession? _session;
        private BertTokenizer? _tokenizer;
        private const int MaxTokenLength = 128; // MiniLM max is 256, but 128 is enough for sentences
        private const int BatchSize = 32;       // Max texts per ONNX forward pass

        public EmbeddingService(INlpModelManager modelManager)
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

                var vocabPath = _modelManager.GetTokenizerPath(NlpModelManager.EmbeddingModelName);
                _tokenizer = BertTokenizer.Create(vocabPath, new BertOptions
                {
                    LowerCaseBeforeTokenization = true
                });

                ct.ThrowIfCancellationRequested();

                var modelPath = _modelManager.GetModelPath(NlpModelManager.EmbeddingModelName);
                var options = new SessionOptions();
                options.InterOpNumThreads = 1;
                options.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2);
                _session = new InferenceSession(modelPath, options);
            }, ct);
        }

        public void UnloadModel()
        {
            _session?.Dispose();
            _session = null;
            _tokenizer = null;
        }

        public float[] ComputeEmbedding(string text)
        {
            if (!IsLoaded)
                throw new InvalidOperationException("Model not loaded. Call LoadModelAsync first.");

            var encoded = EncodeText(text);
            var outputs = RunInference(encoded.inputIds, encoded.attentionMask, encoded.tokenTypeIds, 1, encoded.length);

            // Mean pooling over token embeddings (ignoring padding)
            return MeanPool(outputs, encoded.attentionMask, encoded.length);
        }

        public float[][] ComputeEmbeddings(IReadOnlyList<string> texts)
        {
            if (!IsLoaded)
                throw new InvalidOperationException("Model not loaded. Call LoadModelAsync first.");

            if (texts.Count == 0) return [];
            if (texts.Count == 1) return [ComputeEmbedding(texts[0])];

            var allResults = new float[texts.Count][];

            // Process in batches for efficient ONNX inference
            for (int batchStart = 0; batchStart < texts.Count; batchStart += BatchSize)
            {
                int batchEnd = Math.Min(batchStart + BatchSize, texts.Count);
                int currentBatch = batchEnd - batchStart;

                // Encode all texts in this batch
                var encodings = new (long[] inputIds, long[] attentionMask, long[] tokenTypeIds, int length)[currentBatch];
                int maxLen = 0;
                for (int i = 0; i < currentBatch; i++)
                {
                    encodings[i] = EncodeText(texts[batchStart + i]);
                    maxLen = Math.Max(maxLen, encodings[i].length);
                }

                // Build padded tensors [currentBatch, maxLen]
                var batchInputIds = new long[currentBatch * maxLen];
                var batchAttention = new long[currentBatch * maxLen];
                var batchTokenTypes = new long[currentBatch * maxLen];

                for (int i = 0; i < currentBatch; i++)
                {
                    var (ids, mask, ttids, len) = encodings[i];
                    int offset = i * maxLen;
                    Array.Copy(ids, 0, batchInputIds, offset, len);
                    Array.Copy(mask, 0, batchAttention, offset, len);
                    Array.Copy(ttids, 0, batchTokenTypes, offset, len);
                    // Remaining positions are already 0 (padding)
                }

                // Single ONNX forward pass for the entire batch
                var hiddenStates = RunInference(batchInputIds, batchAttention, batchTokenTypes, currentBatch, maxLen);

                // Extract per-sequence embeddings via mean pooling
                int embDim = hiddenStates.Length / (currentBatch * maxLen);
                for (int i = 0; i < currentBatch; i++)
                {
                    // Extract hidden states for this sequence (including padding positions)
                    var seqHidden = new float[maxLen * embDim];
                    Array.Copy(hiddenStates, i * maxLen * embDim, seqHidden, 0, maxLen * embDim);

                    // Build padded attention mask — MeanPool skips positions with mask=0
                    var paddedMask = new long[maxLen];
                    Array.Copy(encodings[i].attentionMask, 0, paddedMask, 0, encodings[i].length);

                    allResults[batchStart + i] = MeanPool(seqHidden, paddedMask, maxLen);
                }
            }

            return allResults;
        }

        private (long[] inputIds, long[] attentionMask, long[] tokenTypeIds, int length) EncodeText(string text)
        {
            // Encode with truncation
            var ids = _tokenizer!.EncodeToIds(text, MaxTokenLength, out _, out _, considerPreTokenization: true,
                considerNormalization: true);

            // Build [CLS] ids [SEP] sequence
            var fullIds = _tokenizer.BuildInputsWithSpecialTokens(ids);
            var tokenTypeIds = _tokenizer.CreateTokenTypeIdsFromSequences(ids);

            int seqLen = fullIds.Count;
            var inputIds = new long[seqLen];
            var attentionMask = new long[seqLen];
            var ttIds = new long[seqLen];

            for (int i = 0; i < seqLen; i++)
            {
                inputIds[i] = fullIds[i];
                attentionMask[i] = 1;
                ttIds[i] = tokenTypeIds[i];
            }

            return (inputIds, attentionMask, ttIds, seqLen);
        }

        private float[] RunInference(long[] inputIds, long[] attentionMask, long[] tokenTypeIds,
            int batchSize, int seqLength)
        {
            var shape = new[] { batchSize, seqLength };

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids",
                    new DenseTensor<long>(inputIds, shape)),
                NamedOnnxValue.CreateFromTensor("attention_mask",
                    new DenseTensor<long>(attentionMask, shape)),
                NamedOnnxValue.CreateFromTensor("token_type_ids",
                    new DenseTensor<long>(tokenTypeIds, shape))
            };

            using var results = _session!.Run(inputs);

            // MiniLM outputs: last_hidden_state [batch, seq, 384]
            var output = results.First();
            var value = output.Value;

            // OnnxRuntime may return DenseTensor<float> or OrtValue — handle both
            if (value is DenseTensor<float> denseTensor)
                return denseTensor.Buffer.Span.ToArray();

            // Fallback: cast to Tensor<float> and copy element-by-element
            var tensor = output.AsTensor<float>();
            var data = new float[tensor.Length];
            int idx = 0;
            foreach (var val in tensor)
                data[idx++] = val;
            return data;
        }

        private static float[] MeanPool(float[] hiddenStates, long[] attentionMask, int seqLength)
        {
            // hiddenStates shape: [seqLength, embeddingDim]
            int embeddingDim = hiddenStates.Length / seqLength;
            var pooled = new float[embeddingDim];

            int validTokens = 0;
            for (int t = 0; t < seqLength; t++)
            {
                if (attentionMask[t] == 0) continue;
                validTokens++;
                int offset = t * embeddingDim;
                for (int d = 0; d < embeddingDim; d++)
                    pooled[d] += hiddenStates[offset + d];
            }

            if (validTokens > 0)
            {
                for (int d = 0; d < embeddingDim; d++)
                    pooled[d] /= validTokens;
            }

            // L2 normalize
            float norm = 0;
            for (int d = 0; d < embeddingDim; d++)
                norm += pooled[d] * pooled[d];
            norm = MathF.Sqrt(norm);

            if (norm > 0)
            {
                for (int d = 0; d < embeddingDim; d++)
                    pooled[d] /= norm;
            }

            return pooled;
        }

        public static float CosineSimilarityStatic(float[] a, float[] b)
        {
            if (a.Length != b.Length)
                throw new ArgumentException("Vectors must have the same length.");

            float dot = 0, normA = 0, normB = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }

            float denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
            return denom > 0 ? dot / denom : 0;
        }

        public void Dispose()
        {
            _session?.Dispose();
            _session = null;
        }
    }
}
