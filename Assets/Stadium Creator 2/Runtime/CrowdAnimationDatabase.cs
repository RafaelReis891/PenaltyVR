using System;
using UnityEngine;

namespace AiKodex.StadiumCreator2
{
    /// <summary>
    /// Baked crowd animation library.
    /// Representation: for every clip frame and bone, the bone's world-space rotation *delta from the bind pose*
    /// (quaternion), plus the hips position. Because the deltas are expressed in character space and relative
    /// to the shared MPFB bind pose, the same data animates every body variant: the GPU re-runs forward
    /// kinematics with each variant's own bone offsets, so tall / short / heavy bodies keep their proportions.
    /// Memory: frames * bones * 16 bytes (≈ 5-8 MB for ~30 clips), shared by the whole crowd.
    /// </summary>
    [PreferBinarySerialization]
    [CreateAssetMenu(menuName = "Crowd/Animation Database", fileName = "CrowdAnimationDatabase")]
    public class CrowdAnimationDatabase : ScriptableObject
    {
        [Serializable]
        public class Clip
        {
            public string name;
            public string source;
            public CrowdAnimCategory category;
            public int frameOffset;
            public int frameCount;
            public float fps = 30f;
            public bool enabled = true;
            [Tooltip("Forward offset (m) applied to the root while this clip plays (standing clips step out of the seat)")]
            public float rootForward;
            [Tooltip("0..1 perceived energy, used to scale playback speed with crowd intensity")]
            public float energy = 0.5f;
            public float Duration => frameCount / fps;
            public CrowdPosture Posture => CrowdAnimCategories.IsStanding(category) ? CrowdPosture.Standing : CrowdPosture.Sitting;
        }

        public string[] boneNames;
        public float referenceHipsHeight;
        public Clip[] clips;

        [Header("Animation sets")]
        [Tooltip("One baked copy of every clip per reference body (e.g. male / female) for more accurate retargeting")]
        public string[] setNames;
        public float[] setHipsHeights;
        public int framesPerSet;
        public int SetCount => setNames != null && setNames.Length > 0 ? setNames.Length : 1;
        public float SetHipsHeight(int set) => setHipsHeights != null && set < setHipsHeights.Length ? setHipsHeights[set] : referenceHipsHeight;

        [HideInInspector] public Vector4[] rotations;   // frames * boneCount
        [HideInInspector] public Vector4[] hips;        // frames (xyz = hips position)
        public int totalFrames;

        public int BoneCount => boneNames?.Length ?? 0;
        public bool IsValid => clips != null && clips.Length > 0 && rotations != null && rotations.Length > 0;
    }
}
