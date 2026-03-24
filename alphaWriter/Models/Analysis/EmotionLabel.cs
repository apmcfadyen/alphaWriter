namespace alphaWriter.Models.Analysis
{
    /// <summary>
    /// The 28 GoEmotions fine-grained emotion labels from SamLowe/roberta-base-go_emotions.
    /// </summary>
    public enum EmotionLabel
    {
        Admiration,
        Amusement,
        Anger,
        Annoyance,
        Approval,
        Caring,
        Confusion,
        Curiosity,
        Desire,
        Disappointment,
        Disapproval,
        Disgust,
        Embarrassment,
        Excitement,
        Fear,
        Gratitude,
        Grief,
        Joy,
        Love,
        Nervousness,
        Optimism,
        Pride,
        Realization,
        Relief,
        Remorse,
        Sadness,
        Surprise,
        Neutral
    }

    /// <summary>
    /// Seven macro-categories that group the 28 GoEmotions labels for high-level reporting.
    /// </summary>
    public enum EmotionCluster
    {
        Joy,       // admiration, amusement, approval, excitement, gratitude, joy, love, optimism, pride, relief
        Sadness,   // disappointment, grief, remorse, sadness
        Anger,     // anger, annoyance, disapproval, disgust
        Fear,      // embarrassment, fear, nervousness
        Surprise,  // confusion, curiosity, realization, surprise
        Caring,    // caring, desire
        Neutral    // neutral
    }

    public static class EmotionExtensions
    {
        public static EmotionCluster ToCluster(this EmotionLabel label) => label switch
        {
            EmotionLabel.Admiration or EmotionLabel.Amusement or EmotionLabel.Approval or
            EmotionLabel.Excitement or EmotionLabel.Gratitude or EmotionLabel.Joy or
            EmotionLabel.Love or EmotionLabel.Optimism or EmotionLabel.Pride or
            EmotionLabel.Relief => EmotionCluster.Joy,

            EmotionLabel.Disappointment or EmotionLabel.Grief or
            EmotionLabel.Remorse or EmotionLabel.Sadness => EmotionCluster.Sadness,

            EmotionLabel.Anger or EmotionLabel.Annoyance or
            EmotionLabel.Disapproval or EmotionLabel.Disgust => EmotionCluster.Anger,

            EmotionLabel.Embarrassment or EmotionLabel.Fear or
            EmotionLabel.Nervousness => EmotionCluster.Fear,

            EmotionLabel.Confusion or EmotionLabel.Curiosity or
            EmotionLabel.Realization or EmotionLabel.Surprise => EmotionCluster.Surprise,

            EmotionLabel.Caring or EmotionLabel.Desire => EmotionCluster.Caring,

            _ => EmotionCluster.Neutral
        };
    }
}
