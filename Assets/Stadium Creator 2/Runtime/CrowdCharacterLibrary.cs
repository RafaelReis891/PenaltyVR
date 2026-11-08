using System;
using UnityEngine;

namespace AiKodex.StadiumCreator2
{
    /// <summary>
    /// GPU-ready library of pre-authored crowd characters (output of the offline authoring pipeline).
    /// All variants share one skeleton topology. Meshes for one LOD level are concatenated into a single
    /// Mesh so that one multi-command indirect draw renders every variant of that LOD.
    /// Binary serialisation keeps the (large) mesh and texture-array sub-assets compact.
    /// </summary>
    [PreferBinarySerialization]
    [CreateAssetMenu(menuName = "Crowd/Character Library", fileName = "CrowdCharacterLibrary")]
    public class CrowdCharacterLibrary : ScriptableObject
    {
        [Serializable]
        public class Variant
        {
            public string name;
            public bool female;
            public string ageGroup;
            public string category;          // casual / business / sports
            public bool allowKit = true;     // may be recoloured into a team kit
            public int animSet;              // index into the animation database sets (e.g. 0 male, 1 female)
            public float height;          // metres, rest pose
            public float hipsHeight;      // metres, rest pose
            public bool hasTintableTop;   // a team-kit garment exists (part id 1)
            public bool hasTintableBottom;
            public bool hasHat;
            public Vector3[] bindPositions; // per bone, character space (rest pose bone heads)
            public float topMeanLum = 0.5f;    // mean luminance of the tintable garment texels (for tint normalisation)
            public float bottomMeanLum = 0.5f;
            public float hairMeanLum = 0.3f;
            [Tooltip("Relative selection weight when populating a crowd")] public float weight = 1f;
        }

        [Serializable]
        public struct SubMeshRange
        {
            public int indexStart;
            public int indexCount;
            public int baseVertex;
            public int vertexCount;
            [Tooltip("Opaque triangles come first in the range; the remaining (indexCount - opaqueIndexCount) indices are alpha-tested hair cards")]
            public int opaqueIndexCount;
        }

        [Serializable]
        public class Lod
        {
            public Mesh mesh;                 // concatenated mesh for all variants
            public SubMeshRange[] ranges;     // per variant
        }

        public string[] boneNames;
        public int[] boneParents;
        public Variant[] variants;
        public Lod[] lods;
        public Texture2DArray albedoArray;
        public Texture2DArray normalArray;

        public int BoneCount => boneNames?.Length ?? 0;
        public int VariantCount => variants?.Length ?? 0;
        public int LodCount => lods?.Length ?? 0;

        public bool IsValid => variants != null && variants.Length > 0 && lods != null && lods.Length > 0 && albedoArray != null;
    }
}
