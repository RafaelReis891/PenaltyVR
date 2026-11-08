using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AiKodex.StadiumCreator2
{
    /// <summary>
    /// Owns every GPU resource of one crowd and records the per-frame work:
    ///   1. Behaviour  – per-spectator animation selection / emotion reactions (compute)
    ///   2. ResetArgs + Cull – frustum + distance culling, LOD selection, per-(LOD,variant) draw lists (compute)
    ///   3. BuildDispatch + Palette – skinning palettes for visible spectators only (indirect compute)
    ///   4. One multi-command RenderMeshIndirect per LOD (all variants of a LOD in one call)
    /// CPU cost per frame is O(LODs), independent of the number of spectators.
    /// </summary>
    public sealed class CrowdRenderer : IDisposable
    {
        public const int MaxBones = 64;
        public static bool DebugSkipHair, DebugSkipOpaque;
        public static int DebugBone;
        public static bool DebugFreezeBehaviour;

        readonly CrowdCharacterLibrary _lib;
        readonly CrowdAnimationDatabase _db;
        readonly ComputeShader _cs;
        readonly Material[] _lodMaterials;        // opaque (no alpha test -> early-Z)
        readonly Material[] _lodCutoutMaterials;  // alpha-tested hair cards

        GraphicsBuffer _instances, _animState, _clips, _emotion, _catRanges, _catClips, _animRot, _animHips,
            _bindPos, _boneParents, _regionOffsets, _drawArgs, _drawList, _visibleList, _counters, _dispatchArgs,
            _palette, _variantInfo;

        int _count, _variants, _lods, _bones, _commands;
        int _kBehaviour, _kReset, _kCull, _kDispatch, _kPalette;
        readonly Plane[] _planes = new Plane[6];
        readonly Vector4[] _planeVecs = new Vector4[6];

        public int InstanceCount => _count;
        public int CommandCount => _commands;
        public long GpuMemoryBytes { get; private set; }
        public bool IsCreated => _instances != null;

        public CrowdRenderer(CrowdCharacterLibrary lib, CrowdAnimationDatabase db, ComputeShader cs, Material[] lodMaterials, Material[] lodCutoutMaterials)
        {
            _lib = lib; _db = db; _lodMaterials = lodMaterials; _lodCutoutMaterials = lodCutoutMaterials;
            // private instance: several crowds may exist and each binds its own buffers
            _cs = UnityEngine.Object.Instantiate(cs);
            _cs.hideFlags = HideFlags.HideAndDontSave;
            cs = _cs;
            if (lib.BoneCount != db.BoneCount)
                throw new InvalidOperationException($"Character library has {lib.BoneCount} bones but animation database has {db.BoneCount}");
            if (lib.BoneCount > MaxBones) throw new InvalidOperationException("Too many bones");
            _kBehaviour = cs.FindKernel("Behaviour");
            _kReset = cs.FindKernel("ResetArgs");
            _kCull = cs.FindKernel("Cull");
            _kDispatch = cs.FindKernel("BuildDispatch");
            _kPalette = cs.FindKernel("Palette");
        }

        GraphicsBuffer Make(GraphicsBuffer.Target t, int count, int stride)
        {
            var b = new GraphicsBuffer(t, Math.Max(1, count), stride);
            GpuMemoryBytes += (long)Math.Max(1, count) * stride;
            return b;
        }

        public void Create(CrowdInstanceGPU[] instances, CrowdAnimStateGPU[] states)
        {
            Dispose();
            GpuMemoryBytes = 0;
            _count = instances.Length;
            _variants = _lib.VariantCount;
            _lods = Mathf.Min(_lib.LodCount, 4);
            _bones = _lib.BoneCount;
            _commands = _variants * _lods;

            _instances = Make(GraphicsBuffer.Target.Structured, _count, CrowdInstanceGPU.Stride);
            _instances.SetData(instances);
            _animState = Make(GraphicsBuffer.Target.Structured, _count, CrowdAnimStateGPU.Stride);
            _animState.SetData(states);

            // ---- animation data
            var clips = new CrowdClipGPU[_db.clips.Length];
            var perCat = new List<uint>[CrowdAnimCategories.Count];
            for (int c = 0; c < perCat.Length; c++) perCat[c] = new List<uint>();
            for (int i = 0; i < clips.Length; i++)
            {
                var c = _db.clips[i];
                clips[i] = new CrowdClipGPU
                {
                    frameOffset = (uint)c.frameOffset, frameCount = (uint)c.frameCount, fps = c.fps, duration = c.Duration,
                    category = (uint)c.category, posture = (uint)c.Posture, rootForward = c.rootForward, energy = c.energy
                };
                if (c.enabled) perCat[(int)c.category].Add((uint)i);
            }
            _clips = Make(GraphicsBuffer.Target.Structured, clips.Length, CrowdClipGPU.Stride);
            _clips.SetData(clips);
            var ranges = new uint[CrowdAnimCategories.Count * 2];
            var catClips = new List<uint>();
            for (int c = 0; c < perCat.Length; c++)
            {
                ranges[c * 2] = (uint)catClips.Count;
                ranges[c * 2 + 1] = (uint)perCat[c].Count;
                catClips.AddRange(perCat[c]);
            }
            _catRanges = Make(GraphicsBuffer.Target.Structured, CrowdAnimCategories.Count, 8);
            _catRanges.SetData(ranges);
            _catClips = Make(GraphicsBuffer.Target.Structured, Math.Max(1, catClips.Count), 4);
            _catClips.SetData(catClips.Count > 0 ? catClips.ToArray() : new uint[1]);
            _animRot = Make(GraphicsBuffer.Target.Structured, _db.rotations.Length, 16);
            _animRot.SetData(_db.rotations);
            _animHips = Make(GraphicsBuffer.Target.Structured, _db.hips.Length, 16);
            _animHips.SetData(_db.hips);
            _emotion = Make(GraphicsBuffer.Target.Structured, 2, CrowdEmotionGPU.Stride);

            // ---- skeleton per variant
            var bind = new Vector4[_variants * _bones];
            var vinfo = new Vector4[_variants];
            for (int v = 0; v < _variants; v++)
            {
                var vr = _lib.variants[v];
                int set = Mathf.Clamp(vr.animSet, 0, Mathf.Max(0, _db.SetCount - 1));
                float refHips = _db.SetHipsHeight(set);
                float hipsScale = refHips > 0 ? vr.hipsHeight / refHips : 1f;
                float setOffset = set * _db.framesPerSet;
                for (int b = 0; b < _bones; b++)
                {
                    Vector3 p = vr.bindPositions[b];
                    bind[v * _bones + b] = new Vector4(p.x, p.y, p.z, b == 0 ? hipsScale : b == 1 ? setOffset : 0);
                }
                vinfo[v] = new Vector4(vr.topMeanLum, vr.bottomMeanLum, vr.hairMeanLum, 0);
            }
            _bindPos = Make(GraphicsBuffer.Target.Structured, bind.Length, 16);
            _bindPos.SetData(bind);
            _variantInfo = Make(GraphicsBuffer.Target.Structured, _variants, 16);
            _variantInfo.SetData(vinfo);
            _boneParents = Make(GraphicsBuffer.Target.Structured, _bones, 4);
            _boneParents.SetData(_lib.boneParents);

            // ---- draw commands: one region per (lod, variant) sized by that variant's population
            var perVariant = new int[_variants];
            foreach (var i in instances) perVariant[i.variant]++;
            var offsets = new uint[_commands];
            // commands [0, _commands): opaque triangles; [_commands, 2*_commands): alpha-tested hair cards (same instances)
            var args = new GraphicsBuffer.IndirectDrawIndexedArgs[_commands * 2];
            uint running = 0;
            for (int l = 0; l < _lods; l++)
            for (int v = 0; v < _variants; v++)
            {
                int cmd = l * _variants + v;
                var r = _lib.lods[l].ranges[v];
                offsets[cmd] = running;
                int opaque = r.opaqueIndexCount > 0 ? r.opaqueIndexCount : r.indexCount;
                args[cmd] = new GraphicsBuffer.IndirectDrawIndexedArgs
                {
                    indexCountPerInstance = (uint)opaque, instanceCount = 0, startIndex = (uint)r.indexStart,
                    baseVertexIndex = (uint)r.baseVertex, startInstance = running
                };
                args[_commands + cmd] = new GraphicsBuffer.IndirectDrawIndexedArgs
                {
                    indexCountPerInstance = (uint)(r.indexCount - opaque), instanceCount = 0, startIndex = (uint)(r.indexStart + opaque),
                    baseVertexIndex = (uint)r.baseVertex, startInstance = running
                };
                running += (uint)perVariant[v];
            }
            _regionOffsets = Make(GraphicsBuffer.Target.Structured, _commands, 4);
            _regionOffsets.SetData(offsets);
            _drawArgs = Make(GraphicsBuffer.Target.IndirectArguments, _commands * 2, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            _drawArgs.SetData(args);
            _drawList = Make(GraphicsBuffer.Target.Structured, (int)Math.Max(1, running), 4);
            _visibleList = Make(GraphicsBuffer.Target.Structured, _count, 4);
            _counters = Make(GraphicsBuffer.Target.Raw, 4, 4);
            _dispatchArgs = Make(GraphicsBuffer.Target.IndirectArguments, 3, 4);
            _dispatchArgs.SetData(new uint[] { 0, 1, 1 });
            _palette = Make(GraphicsBuffer.Target.Structured, _count * _bones * 2, 16);

            BindAll();
        }

        void BindAll()
        {
            var cs = _cs;
            foreach (int k in new[] { _kBehaviour, _kReset, _kCull, _kDispatch, _kPalette })
            {
                cs.SetBuffer(k, "_Instances", _instances);
                cs.SetBuffer(k, "_AnimState", _animState);
                cs.SetBuffer(k, "_Clips", _clips);
                cs.SetBuffer(k, "_Emotion", _emotion);
                cs.SetBuffer(k, "_CategoryRanges", _catRanges);
                cs.SetBuffer(k, "_CategoryClips", _catClips);
                cs.SetBuffer(k, "_AnimRot", _animRot);
                cs.SetBuffer(k, "_AnimHips", _animHips);
                cs.SetBuffer(k, "_BindPos", _bindPos);
                cs.SetBuffer(k, "_BoneParents", _boneParents);
                cs.SetBuffer(k, "_RegionOffsets", _regionOffsets);
                cs.SetBuffer(k, "_DrawArgs", _drawArgs);
                cs.SetBuffer(k, "_DrawList", _drawList);
                cs.SetBuffer(k, "_VisibleList", _visibleList);
                cs.SetBuffer(k, "_Counters", _counters);
                cs.SetBuffer(k, "_DispatchArgs", _dispatchArgs);
                cs.SetBuffer(k, "_Palette", _palette);
            }
            foreach (var m in System.Linq.Enumerable.Concat(_lodMaterials, _lodCutoutMaterials))
            {
                m.SetBuffer("_Instances", _instances);
                m.SetBuffer("_DrawList", _drawList);
                m.SetBuffer("_Palette", _palette);
                m.SetBuffer("_VariantInfo", _variantInfo);
                m.SetFloat("_BoneCountF", _bones);
                m.SetTexture("_AlbedoArray", _lib.albedoArray);
                if (_lib.normalArray) m.SetTexture("_NormalArray", _lib.normalArray);
            }
        }

        public void SetEmotion(CrowdEmotionGPU previous, CrowdEmotionGPU current)
        {
            if (_emotion == null) return;
            _emotion.SetData(new[] { previous, current });
        }

        public struct FrameSettings
        {
            public Camera camera;
            public float time;
            public float emotionTime;
            public uint epoch;
            public Vector4 wave;
            public Vector4 lodDistances; // LOD0..2 max distance, w = cull distance
            public bool frustumCulling;
            public int shadowMaxLod;      // LODs <= this cast shadows (-1 = none)
            public int layer;
            public bool receiveShadows;
            public int debugPose;
        }

        static readonly Bounds HugeBounds = new Bounds(Vector3.zero, Vector3.one * 100000f);

        public void Render(in FrameSettings fs)
        {
            if (!IsCreated || _count == 0) return;
            var cs = _cs;
            cs.SetInt("_InstanceCount", _count);
            cs.SetInt("_BoneCount", _bones);
            cs.SetInt("_VariantCount", _variants);
            cs.SetInt("_LodCount", _lods);
            cs.SetInt("_CommandCount", _commands * 2);
            cs.SetInt("_HairCommandOffset", _commands);
            cs.SetFloat("_CrowdTime", fs.time);
            cs.SetFloat("_EmotionTime", fs.emotionTime);
            cs.SetInt("_Epoch", (int)fs.epoch);
            cs.SetVector("_WaveOrigin", fs.wave);
            cs.SetVector("_LodDistances", fs.lodDistances);
            cs.SetFloat("_CullRadius", 1.15f);
            cs.SetInt("_DebugPose", fs.debugPose);
            cs.SetInt("_DebugBone", DebugBone);

            Vector3 camPos = fs.camera ? fs.camera.transform.position : Vector3.zero;
            if (fs.camera && fs.frustumCulling)
            {
                GeometryUtility.CalculateFrustumPlanes(fs.camera, _planes);
                for (int i = 0; i < 6; i++) _planeVecs[i] = new Vector4(_planes[i].normal.x, _planes[i].normal.y, _planes[i].normal.z, _planes[i].distance);
            }
            else
            {
                for (int i = 0; i < 6; i++) _planeVecs[i] = new Vector4(0, 0, 0, 1e9f);
            }
            cs.SetVectorArray("_FrustumPlanes", _planeVecs);
            cs.SetVector("_CameraPos", camPos);

            int groups = (_count + 63) / 64;
            if (!DebugFreezeBehaviour) cs.Dispatch(_kBehaviour, groups, 1, 1);
            cs.Dispatch(_kReset, Math.Max(1, (_commands * 2 + 63) / 64), 1, 1);
            cs.Dispatch(_kCull, groups, 1, 1);
            cs.Dispatch(_kDispatch, 1, 1, 1);
            cs.DispatchIndirect(_kPalette, _dispatchArgs, 0);

            for (int l = 0; l < _lods; l++)
            {
                var mesh = _lib.lods[l].mesh;
                if (!mesh) continue;
                var shadows = l <= fs.shadowMaxLod ? ShadowCastingMode.On : ShadowCastingMode.Off;
                var rp = new RenderParams(_lodMaterials[Mathf.Min(l, _lodMaterials.Length - 1)])
                {
                    worldBounds = HugeBounds, shadowCastingMode = shadows, receiveShadows = fs.receiveShadows, layer = fs.layer,
                };
                if (!DebugSkipOpaque) Graphics.RenderMeshIndirect(rp, mesh, _drawArgs, _variants, l * _variants);
                var rc = new RenderParams(_lodCutoutMaterials[Mathf.Min(l, _lodCutoutMaterials.Length - 1)])
                {
                    worldBounds = HugeBounds, shadowCastingMode = shadows, receiveShadows = fs.receiveShadows, layer = fs.layer,
                };
                if (!DebugSkipHair) Graphics.RenderMeshIndirect(rc, mesh, _drawArgs, _variants, _commands + l * _variants);
            }
        }

        /// <summary>Debug read-back (stalls the GPU – profiling/inspection only).</summary>
        public void ReadVisibleCounts(int[] perLod)
        {
            if (!IsCreated) return;
            var args = new GraphicsBuffer.IndirectDrawIndexedArgs[_commands];
            _drawArgs.GetData(args);
            Array.Clear(perLod, 0, perLod.Length);
            for (int c = 0; c < _commands; c++) perLod[Math.Min(c / _variants, perLod.Length - 1)] += (int)args[c].instanceCount;
        }

        public void ReadAnimStates(CrowdAnimStateGPU[] dst) => _animState?.GetData(dst);
        public uint[] ReadDispatchArgs() { var d = new uint[3]; _dispatchArgs?.GetData(d); var c = new uint[1]; _counters?.GetData(c); return new[] { d[0], d[1], d[2], c[0] }; }

        /// <summary>Debug read-back of one spectator's skinning palette (quaternion, translation per bone).</summary>
        public Vector4[] ReadPalette(int instance)
        {
            var d = new Vector4[_bones * 2];
            _palette?.GetData(d, 0, instance * _bones * 2, _bones * 2);
            return d;
        }

        public void Dispose()
        {
            void D(ref GraphicsBuffer b) { b?.Dispose(); b = null; }
            D(ref _instances); D(ref _animState); D(ref _clips); D(ref _emotion); D(ref _catRanges); D(ref _catClips);
            D(ref _animRot); D(ref _animHips); D(ref _bindPos); D(ref _boneParents); D(ref _regionOffsets);
            D(ref _drawArgs); D(ref _drawList); D(ref _visibleList); D(ref _counters); D(ref _dispatchArgs);
            D(ref _palette); D(ref _variantInfo);
        }

        public void Destroy()
        {
            Dispose();
            if (_cs) UnityEngine.Object.DestroyImmediate(_cs);
        }
    }
}
