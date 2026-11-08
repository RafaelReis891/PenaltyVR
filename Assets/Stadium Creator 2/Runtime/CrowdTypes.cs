using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace AiKodex.StadiumCreator2
{
    /// <summary>Overall crowd mood. Drives animation-category probabilities, posture and tempo.</summary>
    public enum CrowdEmotion
    {
        Calm, Interested, Anticipating, Happy, Excited, Cheering, Ecstatic, Disappointed, Sad, Angry, Celebrating
    }

    /// <summary>Animation categories. Every baked clip belongs to exactly one category.
    /// The first <see cref="CrowdAnimCategories.FirstStanding"/> categories are seated.</summary>
    public enum CrowdAnimCategory
    {
        SitIdle = 0, SitClap = 1, SitCheer = 2, SitPoint = 3, SitAngry = 4, SitLaugh = 5, SitDisappointed = 6, SitTalk = 7,
        StandIdle = 8, StandClap = 9, StandCheer = 10, StandFistPump = 11, StandDisappointed = 12, StandJump = 13, StandArmsUp = 14, StandWave = 15,
    }

    public static class CrowdAnimCategories
    {
        public const int Count = 16;
        public const int FirstStanding = 8;
        public static bool IsStanding(CrowdAnimCategory c) => (int)c >= FirstStanding;
    }

    public enum CrowdPosture { Sitting = 0, Standing = 1 }

    public enum KitPattern { Solid = 0, VerticalStripes = 1, Hoops = 2, SleeveBands = 3, ChestBand = 4, Halves = 5 }

    // ------------------------------------------------------------------ GPU structs (keep in sync with CrowdCommon.hlsl)

    /// <summary>Static per-spectator data, written once when the crowd is generated.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CrowdInstanceGPU
    {
        public Vector3 position;   // world position of the feet / seat floor
        public float yaw;          // radians, rotation about +Y (character faces local +Z)
        public uint variant;       // character library variant index
        public float scale;        // uniform scale jitter
        public uint colorTop;      // RGBA8 primary garment colour
        public uint colorTop2;     // RGBA8 secondary kit colour
        public uint colorBottom;   // RGBA8 lower garment colour
        public uint colorHair;     // RGBA8 hair tint (a = tint strength)
        public uint packed;        // [0..7] kit pattern | [8..15] skin tone | [16..23] enthusiasm | [24..31] team
        public float standOffset;  // how far forward (m) a standing spectator steps from the seat position
        public const int Stride = 48;
    }

    /// <summary>Per-spectator animation state, owned and updated by the GPU behaviour kernel.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CrowdAnimStateGPU
    {
        public uint clipA; public float startA; public float speedA; public float blendStart;
        public uint clipB; public float startB; public float speedB; public float blendDuration;
        public float nextDecision; public uint epoch; public float reactAt; public uint flags;
        public const int Stride = 48;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CrowdClipGPU
    {
        public uint frameOffset; public uint frameCount; public float fps; public float duration;
        public uint category; public uint posture; public float rootForward; public float energy;
        public const int Stride = 32;
    }

    /// <summary>Blended emotion parameters uploaded when the mood changes (2 slots: previous and current).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CrowdEmotionGPU
    {
        public float standProbability; public float speed; public float intervalMin; public float intervalMax;
        public float reactDelayMin; public float reactDelayMax; public float blendDuration; public float syncAmount;
        public Vector4 sit0, sit1;     // weights for the 8 seated categories
        public Vector4 stand0, stand1; // weights for the 8 standing categories
        public const int Stride = 96;
    }

    /// <summary>A place where a spectator can be put. Produced by seat sources (bleacher generator, custom lists...).</summary>
    [Serializable]
    public struct CrowdSeat
    {
        public Vector3 position;  // floor point at the seat (feet level), world space
        public Vector3 forward;   // direction the spectator faces (horizontal)
        public int section;
        public int row;
        public float standingBias; // added to stand probability (e.g. supporters' end / back rows)
        public int team;           // -1 = use crowd default split
    }

    public interface ICrowdSeatSource
    {
        /// <summary>Appends seats in world space.</summary>
        void CollectSeats(System.Collections.Generic.List<CrowdSeat> seats);
    }

    public static class CrowdColor
    {
        public static uint Pack(Color c)
        {
            Color32 c32 = c;
            return (uint)c32.r | ((uint)c32.g << 8) | ((uint)c32.b << 16) | ((uint)c32.a << 24);
        }
    }
}
