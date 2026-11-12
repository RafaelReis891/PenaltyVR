using System;
using System.Collections.Generic;
using UnityEngine;

namespace AiKodex.StadiumCreator2
{
    [Serializable]
    public class CrowdTeamKit
    {
        public string name = "Home";
        public Color primary = new Color(0.05f, 0.2f, 0.65f);
        public Color secondary = Color.white;
        public KitPattern pattern = KitPattern.Solid;
        [Tooltip("Fraction of this team's fans wearing the kit (if their outfit allows it)")]
        [Range(0, 1)] public float kitRatio = 0.75f;
        [Tooltip("Fraction of kit wearers using the alternative pattern / colours")]
        [Range(0, 1)] public float alternateRatio = 0.15f;
        public Color alternatePrimary = Color.white;
        public Color alternateSecondary = new Color(0.05f, 0.2f, 0.65f);
        public KitPattern alternatePattern = KitPattern.SleeveBands;
    }

    /// <summary>
    /// Entry point of the crowd system. Gathers seats from seat sources, creates the lightweight per-spectator
    /// GPU instance data (appearance, team, personality), and drives the GPU renderer every frame.
    /// Runtime control: <c>CrowdManager.SetEmotion(CrowdEmotion.Cheering, 1f)</c> or per-instance
    /// <see cref="ApplyEmotion"/> / <see cref="TriggerReaction"/>.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(100)]
    public class CrowdManager : MonoBehaviour
    {
        static readonly List<CrowdManager> s_Active = new List<CrowdManager>();
        public static IReadOnlyList<CrowdManager> Active => s_Active;

        [Header("Assets")]
        public CrowdCharacterLibrary library;
        public CrowdAnimationDatabase animations;
        public ComputeShader simulationShader;
        public Material characterMaterial;

        [Header("Seats")]
        [Tooltip("Components implementing ICrowdSeatSource. Empty = every seat source (e.g. Grandstand) in children")]
        public List<MonoBehaviour> seatSources = new List<MonoBehaviour>();
        [Range(0, 1)] public float density = 1f;
        [Tooltip("Hard cap on spectators (0 = unlimited)")] public int maxSpectators = 0;
        public int seed = 12345;

        [Header("Appearance")]
        public CrowdTeamKit homeTeam = new CrowdTeamKit();
        public CrowdTeamKit awayTeam = new CrowdTeamKit { name = "Away", primary = new Color(0.75f, 0.05f, 0.08f), secondary = Color.white, kitRatio = 0.7f };
        [Range(0, 1)] public float homeRatio = 0.8f;
        [Tooltip("Sections are given to a single team (realistic) instead of mixing fans seat by seat")]
        public bool clusterTeamsBySection = true;
        public Gradient casualTopColors;
        public Color[] casualPalette = {
            new Color(0.12f, 0.12f, 0.13f), new Color(0.55f, 0.55f, 0.57f), new Color(0.9f, 0.9f, 0.88f), new Color(0.13f, 0.18f, 0.3f),
            new Color(0.35f, 0.25f, 0.18f), new Color(0.25f, 0.33f, 0.22f), new Color(0.5f, 0.1f, 0.12f), new Color(0.7f, 0.62f, 0.45f),
            new Color(0.2f, 0.35f, 0.55f), new Color(0.05f, 0.05f, 0.05f) };
        public Color[] bottomPalette = {
            new Color(0.12f, 0.17f, 0.3f), new Color(0.2f, 0.27f, 0.42f), new Color(0.08f, 0.08f, 0.09f), new Color(0.32f, 0.32f, 0.33f),
            new Color(0.52f, 0.45f, 0.33f), new Color(0.16f, 0.2f, 0.16f) };
        public Color[] hairPalette = {
            new Color(0.05f, 0.04f, 0.035f), new Color(0.12f, 0.08f, 0.05f), new Color(0.22f, 0.14f, 0.08f), new Color(0.42f, 0.3f, 0.17f),
            new Color(0.65f, 0.52f, 0.32f), new Color(0.45f, 0.18f, 0.08f) };
        [Range(0, 1)] public float hairTintStrength = 0.3f;
        [Range(0, 1), Tooltip("Fraction of non-kit clothes recoloured from the palettes (variety from few source avatars)")] public float casualRecolorRatio = 0.35f;
        [Range(0, 0.1f)] public float heightJitter = 0.03f;
        [Tooltip("How far (m) standing spectators step forward from their seat")] public float standOffset = 0.22f;

        [Header("Behaviour")]
        public CrowdEmotion emotion = CrowdEmotion.Interested;
        [Range(0, 1)] public float intensity = 0.8f;
        public CrowdEmotionProfile[] emotionProfiles = CrowdEmotionProfile.CreateDefaults();
        [Tooltip("Speed (m/s) at which reactions propagate from a wave origin; 0 = everyone reacts independently")]
        public float defaultWaveSpeed = 0f;

        [Header("Rendering")]
        public Camera cullingCamera;
        public bool frustumCulling = true;
        [Tooltip("Max distance of LOD0, LOD1, LOD2; w = cull distance. Scaled by the camera FOV.")]
        public Vector4 lodDistances = new Vector4(9f, 20f, 50f, 450f);
        [Tooltip("LODs up to this index cast shadows (-1 = none). Crowd shadows are expensive: every caster is drawn into each cascade")] public int shadowCastingMaxLod = 0;
        public bool receiveShadows = true;
        public bool renderInEditMode = true;
        [NonSerialized, HideInInspector, Tooltip("Debug: render every spectator in the bind (rest) pose")] public bool debugBindPose;
        [NonSerialized, HideInInspector, Tooltip("Debug: assign variants in order (library preview)")] public bool debugSequentialVariants;
        [NonSerialized, HideInInspector, Tooltip("Debug: play clip N (and N+1 every debugClipStride spectators) forever")] public int debugForceClip = -1;
        [NonSerialized, HideInInspector] public int debugClipStride = 8;
        [NonSerialized, HideInInspector, Tooltip("Debug: every spectator uses this library variant (-1 = off)")] public int debugForceVariant = -1;

        [Header("Info (read-only)")]
        [SerializeField] int spectatorCount;
        [SerializeField] long gpuBufferMemoryKB;

        CrowdRenderer _renderer;
        Material[] _lodMaterials, _lodCutoutMaterials;
        CrowdInstanceGPU[] _instances;
        CrowdEmotionGPU _prevEmotion, _curEmotion;
        uint _epoch = 1;
        float _emotionTime;
        Vector4 _wave;
        double _timeOrigin;
        float _pendingRevertTime = -1;
        CrowdEmotion _revertEmotion; float _revertIntensity;
        CrowdEmotion _appliedEmotion; float _appliedIntensity = -1;

        public int SpectatorCount => spectatorCount;
        public CrowdRenderer Renderer => _renderer;
        /// <summary>Debug: when >= 0 every crowd uses this time instead of the clock (frame-exact comparisons / captures).</summary>
        public static float DebugTimeOverride = -1f;
        /// <summary>Debug: forces the GPU pose mode for all crowds (-1 = off, 1 = bind pose, 2 = bind + CrowdRenderer.DebugBone rotated).</summary>
        public static int DebugPoseMode = -1;
        public float CrowdTime => DebugTimeOverride >= 0 ? DebugTimeOverride : (float)(CurrentTime() - _timeOrigin);

        // ------------------------------------------------------------------ public API
        /// <summary>Sets the mood of every active crowd.</summary>
        public static void SetEmotion(CrowdEmotion emotion, float intensity = 1f)
        {
            foreach (var m in s_Active) m.ApplyEmotion(emotion, intensity);
        }

        /// <summary>Sets the mood of every active crowd, with reactions spreading from a world position.</summary>
        public static void SetEmotion(CrowdEmotion emotion, float intensity, Vector3 waveOrigin, float waveSpeed = 40f)
        {
            foreach (var m in s_Active) m.ApplyEmotion(emotion, intensity, waveOrigin, waveSpeed);
        }

        /// <summary>Short reaction (e.g. a goal), automatically returning to the previous mood after <paramref name="duration"/>.</summary>
        public static void TriggerReactionAll(CrowdEmotion emotion, float intensity, float duration)
        {
            foreach (var m in s_Active) m.TriggerReaction(emotion, intensity, duration);
        }

        public void ApplyEmotion(CrowdEmotion e, float i) => ApplyEmotionInternal(e, i, Vector4.zero);
        public void ApplyEmotion(CrowdEmotion e, float i, Vector3 origin, float waveSpeed) => ApplyEmotionInternal(e, i, new Vector4(origin.x, origin.y, origin.z, Mathf.Max(0, waveSpeed)));

        public void TriggerReaction(CrowdEmotion e, float i, float duration, Vector3? origin = null, float waveSpeed = 40f)
        {
            if (_pendingRevertTime < 0) { _revertEmotion = emotion; _revertIntensity = intensity; }
            if (origin.HasValue) ApplyEmotion(e, i, origin.Value, waveSpeed); else ApplyEmotion(e, i);
            _pendingRevertTime = CrowdTime + duration;
        }

        void ApplyEmotionInternal(CrowdEmotion e, float i, Vector4 wave)
        {
            emotion = e; intensity = Mathf.Clamp01(i);
            if (wave.w <= 0 && defaultWaveSpeed > 0) wave = new Vector4(transform.position.x, transform.position.y, transform.position.z, defaultWaveSpeed);
            _wave = wave;
            UploadEmotion(true);
        }

        CrowdEmotionProfile Profile(CrowdEmotion e)
        {
            if (emotionProfiles == null || emotionProfiles.Length == 0) emotionProfiles = CrowdEmotionProfile.CreateDefaults();
            foreach (var p in emotionProfiles) if (p.emotion == e) return p;
            return emotionProfiles[0];
        }

        public void ChangeCameraFustrum(Camera camera)
        {
            cullingCamera = camera;
		}

        /// <summary>Scales the stand probability by how much of the mood's standing behaviour actually has clips, so a mood that
        /// mostly wants standing idles does not make people stand up only to clap when no standing idle exists.</summary>
        CrowdEmotionGPU AdjustForAvailableClips(CrowdEmotionGPU e)
        {
            if (!animations || animations.clips == null) return e;
            var has = new bool[CrowdAnimCategories.Count];
            foreach (var c in animations.clips) if (c.enabled) has[(int)c.category] = true;
            float total = 0, avail = 0;
            for (int k = 0; k < 8; k++)
            {
                float w = k < 4 ? e.stand0[k] : e.stand1[k - 4];
                total += w; if (has[CrowdAnimCategories.FirstStanding + k]) avail += w;
            }
            if (total > 0) e.standProbability *= Mathf.Clamp01(avail / total * 1.3f);
            return e;
        }

        void UploadEmotion(bool newEpoch)
        {
            var target = AdjustForAvailableClips(Profile(emotion).ToGPU(Profile(CrowdEmotion.Calm), intensity));
            if (newEpoch)
            {
                _prevEmotion = _appliedIntensity < 0 ? target : _curEmotion;
                _epoch++;
                _emotionTime = CrowdTime;
            }
            _curEmotion = target;
            _appliedEmotion = emotion; _appliedIntensity = intensity;
            _renderer?.SetEmotion(_prevEmotion, _curEmotion);
        }

        // ------------------------------------------------------------------ lifecycle
        void OnEnable()
        {
            s_Active.Add(this);
            _timeOrigin = CurrentTime();
            UnityEngine.Rendering.RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            if (Application.isPlaying || renderInEditMode) Generate();
        }

        void OnDisable()
        {
            s_Active.Remove(this);
            UnityEngine.Rendering.RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            ReleaseGPU();
        }

        /// <summary>When set, crowds are only simulated/submitted through <see cref="RenderNow"/> (used by offline capture tools).</summary>
        public static bool SuppressAutoRender;

        /// <summary>Simulates, culls for <paramref name="cam"/> and submits this crowd immediately (offline rendering / captures).</summary>
        public void RenderNow(Camera cam)
        {
            var prev = cullingCamera;
            cullingCamera = cam;
            RenderFrame();
            cullingCamera = prev;
        }

        int _lastRenderedFrame = -1;
        void OnBeginContextRendering(UnityEngine.Rendering.ScriptableRenderContext ctx, List<Camera> cams)
        {
            if (SuppressAutoRender) return;
            // Submitting here (rather than in Update) uses final camera positions and also works in edit mode.
            if (_lastRenderedFrame == Time.frameCount && Application.isPlaying) return;
            _lastRenderedFrame = Time.frameCount;
            // edit mode: cull / pick LODs for the Scene view camera that is about to render
            _editCamera = null;
            if (!Application.isPlaying && cams != null)
                foreach (var c in cams) if (c && c.cameraType == CameraType.SceneView) { _editCamera = c; break; }
            RenderFrame();
        }

        Camera _editCamera;
        bool _regenRequested;
        float _regenAt;

        /// <summary>Asks for the crowd to be regenerated shortly (seat sources call this when they change).</summary>
        public void RequestRegenerate()
        {
            _regenRequested = true;
            _regenAt = Time.realtimeSinceStartup + 0.15f;
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        void Update()
        {
            if (_regenRequested && Time.realtimeSinceStartup >= _regenAt)
            {
                _regenRequested = false;
                if (Application.isPlaying || renderInEditMode) Generate();
            }
#if UNITY_EDITOR
            else if (_regenRequested && !Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        static double CurrentTime() => Application.isPlaying ? Time.timeAsDouble : (double)Time.realtimeSinceStartup;

        void ReleaseGPU()
        {
            _renderer?.Destroy();
            _renderer = null;
            if (_lodMaterials != null)
                foreach (var m in _lodMaterials) if (m) DestroyImmediate(m);
            if (_lodCutoutMaterials != null)
                foreach (var m in _lodCutoutMaterials) if (m) DestroyImmediate(m);
            _lodCutoutMaterials = null;
            _lodMaterials = null;
        }

        public bool AssetsReady => library && library.IsValid && animations && animations.IsValid && simulationShader && characterMaterial;

        /// <summary>(Re)builds the crowd from the current seat sources and settings.</summary>
        [ContextMenu("Generate Crowd")]
        public void Generate()
        {
            ReleaseGPU();
            if (!AssetsReady) return;
            if (!SystemInfo.supportsComputeShaders) { Debug.LogWarning("CrowdManager: compute shaders not supported"); return; }

            var seats = new List<CrowdSeat>();
            var sources = new List<ICrowdSeatSource>();
            foreach (var mb in seatSources) if (mb is ICrowdSeatSource src) sources.Add(src);
            if (sources.Count == 0) foreach (var mb in GetComponentsInChildren<MonoBehaviour>()) if (mb is ICrowdSeatSource s0 && mb.enabled) sources.Add(s0);
            foreach (var s in sources) s.CollectSeats(seats);

            _instances = BuildInstances(seats);
            spectatorCount = _instances.Length;
            if (_instances.Length == 0) return;

            int lodCount = Mathf.Min(4, library.LodCount);
            _lodMaterials = new Material[lodCount];
            _lodCutoutMaterials = new Material[lodCount];
            for (int l = 0; l < lodCount; l++)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    var m = new Material(characterMaterial) { name = characterMaterial.name + (pass == 0 ? "_Opaque_LOD" : "_Hair_LOD") + l, hideFlags = HideFlags.DontSave };
                    if (l >= 2) { m.DisableKeyword("_CROWD_NORMALMAP"); m.SetFloat("_UseNormalMap", 0); }
                    else { m.EnableKeyword("_CROWD_NORMALMAP"); m.SetFloat("_UseNormalMap", 1); }
                    if (pass == 0) { m.DisableKeyword("_CROWD_ALPHATEST"); m.SetFloat("_AlphaTest", 0); m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry; }
                    else { m.EnableKeyword("_CROWD_ALPHATEST"); m.SetFloat("_AlphaTest", 1); m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest; }
                    // thin hair cards keep their mass at a distance
                    if (l >= 2) m.SetFloat("_Cutoff", Mathf.Max(0.2f, characterMaterial.GetFloat("_Cutoff") - 0.2f));
                    if (pass == 0) _lodMaterials[l] = m; else _lodCutoutMaterials[l] = m;
                }
            }

            _renderer = new CrowdRenderer(library, animations, simulationShader, _lodMaterials, _lodCutoutMaterials);
            _timeOrigin = CurrentTime();
            var states = BuildInitialStates(_instances);
            _renderer.Create(_instances, states);
            gpuBufferMemoryKB = _renderer.GpuMemoryBytes / 1024;
            _appliedIntensity = -1;
            UploadEmotion(true);
            RenderFrame(); // warm-up: simulate + build palettes now so the first rendered frame is never in bind pose
        }

        CrowdInstanceGPU[] BuildInstances(List<CrowdSeat> seats)
        {
            var rnd = new System.Random(seed);
            float R() => (float)rnd.NextDouble();
            var list = new List<CrowdInstanceGPU>(seats.Count);
            float totalW = 0;
            foreach (var v in library.variants) totalW += Mathf.Max(0, v.weight);

            // team per section
            var sectionTeam = new Dictionary<int, int>();
            int lastVariant = -1;
            foreach (var seat in seats)
            {
                if (density < 1f && R() > density) continue;
                if (maxSpectators > 0 && list.Count >= maxSpectators) break;

                int variant = debugForceVariant >= 0 ? debugForceVariant : debugSequentialVariants ? list.Count % library.variants.Length : PickVariant(R() * totalW);
                if (variant == lastVariant && !debugSequentialVariants) variant = PickVariant(R() * totalW);
                lastVariant = variant;
                var vr = library.variants[variant];

                int team = seat.team;
                if (team < 0)
                {
                    if (clusterTeamsBySection)
                    {
                        if (!sectionTeam.TryGetValue(seat.section, out team))
                        {
                            var sr = new System.Random(seed * 31 + seat.section);
                            team = sr.NextDouble() < homeRatio ? 0 : 1;
                            sectionTeam[seat.section] = team;
                        }
                        if (R() < 0.06f) team = 1 - team; // a few away fans in home sections & vice versa
                    }
                    else team = R() < homeRatio ? 0 : 1;
                }
                var kit = team == 0 ? homeTeam : awayTeam;

                Color top, top2; KitPattern pattern = KitPattern.Solid; float topStrength = 1f;
                if (vr.hasTintableTop && R() < kit.kitRatio)
                {
                    bool alt = R() < kit.alternateRatio;
                    top = alt ? kit.alternatePrimary : kit.primary;
                    top2 = alt ? kit.alternateSecondary : kit.secondary;
                    pattern = alt ? kit.alternatePattern : kit.pattern;
                    // slight fading / different replica generations
                    top = Color.Lerp(top, top * (0.85f + 0.3f * R()), 0.5f);
                }
                else
                {
                    // non-kit wearers mostly keep their authored clothes; a share is recoloured for extra variety
                    top = casualPalette[rnd.Next(casualPalette.Length)];
                    top2 = top;
                    topStrength = vr.hasTintableTop && R() < casualRecolorRatio ? 1f : 0f;
                }
                top.a = topStrength;
                Color bottom = bottomPalette[rnd.Next(bottomPalette.Length)]; bottom.a = vr.hasTintableBottom && R() < casualRecolorRatio ? 0.9f : 0f;
                Color hair = vr.ageGroup == "old" ? Color.Lerp(new Color(0.55f, 0.53f, 0.5f), new Color(0.82f, 0.8f, 0.76f), R())
                                                : hairPalette[rnd.Next(hairPalette.Length)];
                hair.a = hairTintStrength;

                float yaw = Mathf.Atan2(seat.forward.x, seat.forward.z) + (R() - 0.5f) * 0.25f;
                float enthusiasm = Mathf.Clamp01(R() * 0.8f + 0.1f + seat.standingBias);
                uint packed = (uint)pattern | ((uint)(R() * 255) << 8) | ((uint)(enthusiasm * 255) << 16) | ((uint)team << 24);

                list.Add(new CrowdInstanceGPU
                {
                    position = seat.position,
                    yaw = yaw,
                    variant = (uint)variant,
                    scale = 1f + (R() * 2 - 1) * heightJitter,
                    colorTop = CrowdColor.Pack(top),
                    colorTop2 = CrowdColor.Pack(new Color(top2.r, top2.g, top2.b, 1)),
                    colorBottom = CrowdColor.Pack(bottom),
                    colorHair = CrowdColor.Pack(hair),
                    packed = packed,
                    standOffset = standOffset,
                });
            }
            return list.ToArray();

            int PickVariant(float r)
            {
                for (int i = 0; i < library.variants.Length; i++)
                {
                    r -= Mathf.Max(0, library.variants[i].weight);
                    if (r <= 0) return i;
                }
                return library.variants.Length - 1;
            }
        }

        CrowdAnimStateGPU[] BuildInitialStates(CrowdInstanceGPU[] inst)
        {
            var rnd = new System.Random(seed ^ 0x5f3759df);
            float R() => (float)rnd.NextDouble();
            var prof = AdjustForAvailableClips(Profile(emotion).ToGPU(Profile(CrowdEmotion.Calm), intensity));
            var byCat = new List<int>[CrowdAnimCategories.Count];
            for (int c = 0; c < byCat.Length; c++) byCat[c] = new List<int>();
            for (int i = 0; i < animations.clips.Length; i++) if (animations.clips[i].enabled) byCat[(int)animations.clips[i].category].Add(i);

            var states = new CrowdAnimStateGPU[inst.Length];
            float[] w = new float[8];
            for (int i = 0; i < inst.Length; i++)
            {
                float enth = 0.5f + ((inst[i].packed >> 16) & 255) / 255f;
                int posture = R() < Mathf.Clamp01(prof.standProbability * enth) ? 1 : 0;
                Vector4 a = posture == 0 ? prof.sit0 : prof.stand0, b = posture == 0 ? prof.sit1 : prof.stand1;
                for (int k = 0; k < 8; k++) w[k] = (k < 4 ? a[k] : b[k - 4]) * (byCat[posture * 8 + k].Count > 0 ? 1 : 0);
                float tot = 0; foreach (var x in w) tot += x;
                int cat = posture * 8;
                if (tot > 0)
                {
                    float r = R() * tot;
                    for (int k = 0; k < 8; k++) { if (w[k] <= 0) continue; cat = posture * 8 + k; if (r < w[k]) break; r -= w[k]; }
                }
                var lst = byCat[cat].Count > 0 ? byCat[cat] : byCat[posture * 8].Count > 0 ? byCat[posture * 8] : byCat[0];
                int clip = lst.Count > 0 ? lst[rnd.Next(lst.Count)] : 0;
                if (debugForceClip >= 0) clip = Mathf.Min(animations.clips.Length - 1, debugForceClip + i / Mathf.Max(1, debugClipStride));
                float dur = animations.clips[clip].Duration;
                float speed = prof.speed * (0.88f + 0.24f * R());
                states[i] = new CrowdAnimStateGPU
                {
                    clipA = (uint)clip, startA = debugForceClip >= 0 ? 0f : -R() * dur / speed, speedA = debugForceClip >= 0 ? 1f : speed, blendStart = 0,
                    clipB = (uint)clip, startB = 0, speedB = speed, blendDuration = 0,
                    nextDecision = debugForceClip >= 0 ? 1e9f : Mathf.Lerp(prof.intervalMin, prof.intervalMax, R()),
                    epoch = _epoch + 1, reactAt = 0, flags = 0,
                };
            }
            return states;
        }

        /// <summary>CPU time (ms) spent by this crowd in the last frame: GPU command recording for simulation, culling and draws.</summary>
        public float LastCpuMs { get; private set; }
        readonly System.Diagnostics.Stopwatch _cpuTimer = new System.Diagnostics.Stopwatch();

        void RenderFrame()
        {
            _cpuTimer.Restart();
            RenderFrameInternal();
            LastCpuMs = (float)_cpuTimer.Elapsed.TotalMilliseconds;
        }

        void RenderFrameInternal()
        {
            if (!Application.isPlaying && !renderInEditMode) return;
            if (_renderer == null) return;

            float now = CrowdTime;
            if (_pendingRevertTime >= 0 && now >= _pendingRevertTime)
            {
                _pendingRevertTime = -1;
                ApplyEmotion(_revertEmotion, _revertIntensity);
            }
            // inspector edits at runtime
            if (_appliedEmotion != emotion || !Mathf.Approximately(_appliedIntensity, intensity)) { _wave = Vector4.zero; UploadEmotion(true); }

            var cam = cullingCamera ? cullingCamera : _editCamera ? _editCamera : Camera.main;
            float fovScale = 1f;
            if (cam) fovScale = Mathf.Tan(30f * Mathf.Deg2Rad) / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var fs = new CrowdRenderer.FrameSettings
            {
                camera = cam,
                time = now,
                emotionTime = _emotionTime,
                epoch = _epoch,
                wave = _wave,
                lodDistances = new Vector4(lodDistances.x * fovScale, lodDistances.y * fovScale, lodDistances.z * fovScale, lodDistances.w),
                frustumCulling = frustumCulling && cam,
                shadowMaxLod = shadowCastingMaxLod,
                layer = gameObject.layer,
                receiveShadows = receiveShadows,
                debugPose = DebugPoseMode >= 0 ? DebugPoseMode : debugBindPose ? 1 : 0,
            };
            _renderer.Render(fs);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            if (_renderer != null && (_appliedEmotion != emotion || !Mathf.Approximately(_appliedIntensity, intensity))) UploadEmotion(true);
        }
#endif
    }
}
