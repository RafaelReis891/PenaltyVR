using System;
using UnityEngine;

namespace AiKodex.StadiumCreator2
{
    /// <summary>
    /// How a crowd behaves for one <see cref="CrowdEmotion"/>. Weights are relative per posture;
    /// categories without baked clips are ignored automatically.
    /// </summary>
    [Serializable]
    public class CrowdEmotionProfile
    {
        public CrowdEmotion emotion;
        [Range(0, 1)] public float standProbability = 0.05f;
        [Tooltip("Playback-speed multiplier")] public float speed = 1f;
        [Tooltip("Seconds between spontaneous re-selections (min/max)")] public Vector2 interval = new Vector2(6, 14);
        [Tooltip("Seconds before a spectator reacts to a mood change (min/max)")] public Vector2 reactionDelay = new Vector2(0.1f, 1.2f);
        [Tooltip("Cross-fade duration between clips")] public float blendDuration = 0.45f;
        [Range(0, 1), Tooltip("0 = fully random clip phases, 1 = everyone starts the reaction clip in sync")] public float sync = 0f;
        [Tooltip("SitIdle, SitClap, SitCheer, SitPoint, SitAngry, SitLaugh, SitDisappointed, SitTalk")]
        public float[] sitWeights = new float[8];
        [Tooltip("StandIdle, StandClap, StandCheer, StandFistPump, StandDisappointed, StandJump, StandArmsUp, StandWave")]
        public float[] standWeights = new float[8];

        static CrowdEmotionProfile P(CrowdEmotion e, float stand, float speed, Vector2 interval, Vector2 delay, float[] sit, float[] st, float blend = 0.45f, float sync = 0f)
            => new CrowdEmotionProfile { emotion = e, standProbability = stand, speed = speed, interval = interval, reactionDelay = delay, sitWeights = sit, standWeights = st, blendDuration = blend, sync = sync };

        //                                         idle  clap cheer point angry laugh disap talk
        public static CrowdEmotionProfile[] CreateDefaults() => new[]
        {
            P(CrowdEmotion.Calm,         0.03f, 0.9f,  new Vector2(8, 18), new Vector2(0.3f, 2.5f), new float[]{14, 0.4f, 0, 0.2f, 0, 0.4f, 0, 1.2f}, new float[]{10, 0.5f, 0, 0, 0, 0, 0, 0}, 0.7f),
            P(CrowdEmotion.Interested,   0.06f, 1.0f,  new Vector2(6, 14), new Vector2(0.2f, 1.6f), new float[]{12, 1f, 0.2f, 1f, 0, 0.6f, 0, 1.2f}, new float[]{8, 1.5f, 0.2f, 0, 0, 0, 0, 0}, 0.6f),
            P(CrowdEmotion.Anticipating, 0.22f, 1.05f, new Vector2(4, 9),  new Vector2(0.1f, 1.0f), new float[]{6, 3, 1, 2, 0, 0, 0, 1}, new float[]{6, 3, 1, 0.5f, 0, 0, 0.3f, 0}, 0.5f),
            P(CrowdEmotion.Happy,        0.12f, 1.0f,  new Vector2(5, 11), new Vector2(0.2f, 1.4f), new float[]{3, 5, 1, 0.5f, 0, 3, 0, 1}, new float[]{3, 5, 1, 0.5f, 0, 0, 0.3f, 0.5f}),
            P(CrowdEmotion.Excited,      0.45f, 1.1f,  new Vector2(4, 9),  new Vector2(0.1f, 0.9f), new float[]{1, 6, 4, 1, 0, 1, 0, 0}, new float[]{1, 6, 4, 2, 0, 0.5f, 1, 0.5f}, 0.4f),
            P(CrowdEmotion.Cheering,     0.7f,  1.15f, new Vector2(3, 7),  new Vector2(0.05f, 0.7f), new float[]{0.3f, 4, 6, 1, 0, 0.5f, 0, 0}, new float[]{0.2f, 4, 7, 4, 0, 1.5f, 3, 1}, 0.35f),
            P(CrowdEmotion.Ecstatic,     0.92f, 1.25f, new Vector2(3, 6),  new Vector2(0.0f, 0.5f), new float[]{0, 2, 8, 0, 0, 0, 0, 0}, new float[]{0, 2, 8, 5, 0, 3, 5, 1}, 0.3f, 0.15f),
            P(CrowdEmotion.Disappointed, 0.05f, 0.85f, new Vector2(5, 12), new Vector2(0.1f, 1.0f), new float[]{3, 0, 0, 0.2f, 1, 0, 8, 0.5f}, new float[]{2, 0, 0, 0, 8, 0, 0, 0}, 0.6f),
            P(CrowdEmotion.Sad,          0.02f, 0.75f, new Vector2(8, 16), new Vector2(0.3f, 2.0f), new float[]{5, 0, 0, 0, 0, 0, 8, 0.5f}, new float[]{4, 0, 0, 0, 6, 0, 0, 0}, 0.8f),
            P(CrowdEmotion.Angry,        0.3f,  1.1f,  new Vector2(3, 8),  new Vector2(0.05f, 0.8f), new float[]{1, 0, 0, 3, 8, 0, 1, 0}, new float[]{1, 0, 0, 4, 3, 0, 0, 0}, 0.4f),
            P(CrowdEmotion.Celebrating,  0.95f, 1.3f,  new Vector2(3, 6),  new Vector2(0.0f, 0.45f), new float[]{0, 2, 8, 0, 0, 1, 0, 0}, new float[]{0, 3, 8, 6, 0, 4, 7, 2}, 0.25f, 0.1f),
        };

        public CrowdEmotionGPU ToGPU(CrowdEmotionProfile baseline, float intensity)
        {
            float t = Mathf.Clamp01(intensity);
            float L(float a, float b) => Mathf.Lerp(a, b, t);
            float W(float[] a, float[] b, int i) => L(a != null && i < a.Length ? a[i] : 0, b != null && i < b.Length ? b[i] : 0);
            var bl = baseline ?? this;
            return new CrowdEmotionGPU
            {
                standProbability = L(bl.standProbability, standProbability),
                speed = L(bl.speed, speed),
                intervalMin = L(bl.interval.x, interval.x),
                intervalMax = L(bl.interval.y, interval.y),
                reactDelayMin = reactionDelay.x,
                reactDelayMax = reactionDelay.y,
                blendDuration = blendDuration,
                syncAmount = sync * t,
                sit0 = new Vector4(W(bl.sitWeights, sitWeights, 0), W(bl.sitWeights, sitWeights, 1), W(bl.sitWeights, sitWeights, 2), W(bl.sitWeights, sitWeights, 3)),
                sit1 = new Vector4(W(bl.sitWeights, sitWeights, 4), W(bl.sitWeights, sitWeights, 5), W(bl.sitWeights, sitWeights, 6), W(bl.sitWeights, sitWeights, 7)),
                stand0 = new Vector4(W(bl.standWeights, standWeights, 0), W(bl.standWeights, standWeights, 1), W(bl.standWeights, standWeights, 2), W(bl.standWeights, standWeights, 3)),
                stand1 = new Vector4(W(bl.standWeights, standWeights, 4), W(bl.standWeights, standWeights, 5), W(bl.standWeights, standWeights, 6), W(bl.standWeights, standWeights, 7)),
            };
        }
    }
}
