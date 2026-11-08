using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AiKodex.StadiumCreator2
{
    public enum GrandstandStyle
    {
        [Tooltip("Solid stepped tiers with plastic bucket seats")] Classic,
        [Tooltip("Wooden plank benches and slatted risers on a steel frame")] WoodenBleacher,
        [Tooltip("Aluminium plank benches on an open steel frame")] SteelBleacher,
        [Tooltip("Solid concrete steps that spectators sit on directly")] ConcreteSlabs,
    }

    public enum GrandstandDeck { Concrete, Wood }

    /// <summary>
    /// One procedural grandstand: <see cref="rows"/> × <see cref="columns"/> seats, optionally curved either way,
    /// in one of four construction styles, with optional railings and aisle stairs. Builds its own mesh and provides
    /// seats to the parent <see cref="CrowdManager"/>. Local space: the front edge is centred on the origin, spectators
    /// face -Z, rows climb towards +Z.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public class Grandstand : MonoBehaviour, ICrowdSeatSource
    {
        [Header("Size")]
        [Range(1, 60)] public int rows = 5;
        [Tooltip("Seats in the front row (rows behind gain seats when the stand curves around the field)")]
        [Range(2, 200)] public int columns = 10;
        [Tooltip("Bend the stand around the field (+) or away from it (-). ±1 = half circle")]
        [Range(-1f, 1f)] public float curve = 0f;

        [Header("Construction")]
        public GrandstandStyle style = GrandstandStyle.Classic;
        [Tooltip("Tier material for the Classic style")] public GrandstandDeck deck = GrandstandDeck.Concrete;
        public bool railings = true;
        [Tooltip("Steps at both ends of every row")] public bool aisleStairs = true;
        public Color seatColor = new Color(0.72f, 0.07f, 0.09f);

        [Header("Spacing")]
        [Tooltip("Distance between neighbouring seats")] [Range(0.45f, 1.5f)] public float seatSpacing = 0.55f;
        [Tooltip("Use the recommended row depth / rise for the chosen style")] public bool autoRowSize = true;
        [Range(0.7f, 1.6f)] public float rowDepth = 0.85f;
        [Range(0.15f, 0.8f)] public float rowRise = 0.38f;

        [Header("Crowd")]
        [Tooltip("Fraction of seats that are occupied")] [Range(0f, 1f)] public float density = 1f;
        [Tooltip("-1: crowd default, 0: home fans, 1: away fans")] [Range(-1, 1)] public int team = -1;
        [Tooltip("Added to the chance that spectators stand")] [Range(0f, 1f)] public float standingBias = 0f;
        public int seed = 1;

        [Header("Materials")]
        public Material concreteMaterial;
        public Material woodMaterial;
        public Material steelMaterial;
        public Material aluminiumMaterial;
        public Material seatMaterial;

        public const float SeatHeight = 0.45f;   // seat surface above the spectator's feet (matches the baked sitting clips)
        const float AisleWidth = 1.0f, EndMargin = 0.12f, RailHeight = 1.0f, Uv = 0.5f;

        [NonSerialized] bool _dirty = true;
        [NonSerialized] Mesh _mesh;
        public event Action Changed;

        // ------------------------------------------------------------------ derived dimensions
        public float Depth => autoRowSize ? StyleDepth(style) : rowDepth;
        public float Rise => style == GrandstandStyle.ConcreteSlabs ? SeatHeight : autoRowSize ? StyleRise(style) : rowRise;
        public float BaseHeight => style == GrandstandStyle.ConcreteSlabs ? SeatHeight : style == GrandstandStyle.Classic ? 0.6f : 0.3f;
        bool Bench => style == GrandstandStyle.WoodenBleacher || style == GrandstandStyle.SteelBleacher;
        float Aisle => aisleStairs ? AisleWidth : 0f;
        /// <summary>Length of the front edge.</summary>
        public float Length => columns * seatSpacing + 2f * (Aisle + EndMargin);
        public float TotalDepth => rows * Depth;
        float RowOffset(int r) => r * Depth;
        float RowHeight(int r) => BaseHeight + r * Rise;

        static float StyleDepth(GrandstandStyle s) => s switch { GrandstandStyle.Classic => 0.85f, GrandstandStyle.ConcreteSlabs => 0.9f, _ => 0.8f };
        static float StyleRise(GrandstandStyle s) => s switch { GrandstandStyle.Classic => 0.38f, GrandstandStyle.ConcreteSlabs => SeatHeight, _ => 0.3f };

        /// <summary>Signed curvature of the front edge (1/m). Clamped so convex stands never fold back on themselves.</summary>
        public float Kappa
        {
            get
            {
                float k = curve * Mathf.PI / Mathf.Max(0.5f, Length);
                return Mathf.Max(k, -1f / (TotalDepth + 1.5f));
            }
        }

        // ------------------------------------------------------------------ path (front edge parameter s, outward offset o)
        public Vector3 Point(float s, float o, float y = 0f)
        {
            float k = Kappa;
            if (Mathf.Abs(k) < 1e-5f) return new Vector3(s, y, o);
            float th = k * s, sn = Mathf.Sin(th), cs = Mathf.Cos(th);
            return new Vector3(sn / k + o * sn, y, -(1f - cs) / k + o * cs);
        }

        public Vector3 Outward(float s) { float th = Kappa * s; return new Vector3(Mathf.Sin(th), 0, Mathf.Cos(th)); }
        public Vector3 Tangent(float s) { float th = Kappa * s; return new Vector3(Mathf.Cos(th), 0, -Mathf.Sin(th)); }
        float Stretch(float o) => Mathf.Max(0.05f, 1f + Kappa * o);   // arc length factor at offset o

        // ------------------------------------------------------------------ seats
        struct SeatSpot { public float s, o, y; }

        /// <summary>Spectator root (feet) positions, local space.</summary>
        void ForEachSeat(Action<int, SeatSpot> f)
        {
            float d = Depth;
            for (int r = 0; r < rows; r++)
            {
                float rootO, rootY;
                switch (style)
                {
                    case GrandstandStyle.ConcreteSlabs: rootO = RowOffset(r) + 0.15f; rootY = RowHeight(r) - SeatHeight; break;
                    case GrandstandStyle.Classic: rootO = RowOffset(r) + d - 0.25f; rootY = RowHeight(r); break;
                    default: rootO = RowOffset(r) + d - 0.25f; rootY = RowHeight(r); break;
                }
                float usable = (Length - 2f * (Aisle + EndMargin)) * Stretch(rootO);
                int n = Mathf.Max(1, Mathf.FloorToInt(usable / seatSpacing + 1e-3f));
                float ds = seatSpacing / Stretch(rootO);
                for (int j = 0; j < n; j++)
                    f(r, new SeatSpot { s = (j + 0.5f - n * 0.5f) * ds, o = rootO, y = rootY });
            }
        }

        public int SeatCount { get { int c = 0; ForEachSeat((r, s) => c++); return c; } }

        public void CollectSeats(List<CrowdSeat> seats)
        {
            var rnd = new System.Random(seed * 7919 + 13);
            var m = transform.localToWorldMatrix;
            int section = 1000 + transform.GetSiblingIndex();
            ForEachSeat((r, sp) =>
            {
                if (density < 1f && rnd.NextDouble() > density) return;
                var p = Point(sp.s, sp.o, sp.y);
                var fwd = m.MultiplyVector(-Outward(sp.s)); fwd.y = 0; fwd.Normalize();
                seats.Add(new CrowdSeat { position = m.MultiplyPoint3x4(p), forward = fwd, section = section, row = r, standingBias = standingBias, team = team });
            });
        }

        // ------------------------------------------------------------------ lifecycle
        void OnEnable() { _dirty = true; transform.hasChanged = false; }
        void OnValidate() { _dirty = true; }
        void OnDisable() { Changed?.Invoke(); NotifyCrowd(); }

        void OnDestroy()
        {
            if (_mesh) { if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh); }
        }

        void Update()
        {
            if (_dirty) Rebuild();
            else if (transform.hasChanged) { transform.hasChanged = false; RefreshNeighbours(); BuildMesh(); NotifyCrowd(); }
        }

        void NotifyCrowd()
        {
            var crowd = GetComponentInParent<CrowdManager>();
            if (crowd) crowd.RequestRegenerate();
        }

        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            _dirty = false;
            if (autoRowSize) { rowDepth = StyleDepth(style); rowRise = StyleRise(style); }
            BuildMesh();
            RefreshNeighbours();
            Changed?.Invoke();
            NotifyCrowd();
        }

        static bool s_Refreshing;
        /// <summary>Neighbours re-evaluate which side railings they need (they may now touch or no longer touch this stand).</summary>
        void RefreshNeighbours()
        {
            if (s_Refreshing || !transform.parent) return;
            s_Refreshing = true;
            try { foreach (var g in transform.parent.GetComponentsInChildren<Grandstand>()) if (g != this && g.transform.parent == transform.parent && g.isActiveAndEnabled) g.BuildMesh(); }
            finally { s_Refreshing = false; }
        }

        // ------------------------------------------------------------------ geometry
        const int SubConcrete = 0, SubWood = 1, SubSteel = 2, SubAluminium = 3, SubSeat = 4;

        class Builder
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int>[] t = { new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>() };

            public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 normal)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                n.Add(normal); n.Add(normal); n.Add(normal); n.Add(normal);
                uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
                // winding so that the face points along 'normal'
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) >= 0) { t[sub].Add(i); t[sub].Add(i + 1); t[sub].Add(i + 2); t[sub].Add(i); t[sub].Add(i + 2); t[sub].Add(i + 3); }
                else { t[sub].Add(i); t[sub].Add(i + 2); t[sub].Add(i + 1); t[sub].Add(i); t[sub].Add(i + 3); t[sub].Add(i + 2); }
            }

            /// <summary>Oriented box. right/up/fwd must be orthonormal; size in those axes.</summary>
            public void Box(int sub, Vector3 c, Vector3 right, Vector3 up, Vector3 fwd, Vector3 size, float vShift = 0f)
            {
                Vector3 hx = right * size.x * 0.5f, hy = up * size.y * 0.5f, hz = fwd * size.z * 0.5f;
                void F(Vector3 nrm, Vector3 a, Vector3 b, float la, float lb)
                {
                    var o = c + nrm;
                    var u0 = new Vector2(0, vShift); var u1 = new Vector2(la * Uv, vShift); var u2 = new Vector2(la * Uv, vShift + lb * Uv); var u3 = new Vector2(0, vShift + lb * Uv);
                    Quad(sub, o - a - b, o + a - b, o + a + b, o - a + b, u0, u1, u2, u3, nrm.normalized);
                }
                F(hy, hx, hz, size.x, size.z); F(-hy, hx, hz, size.x, size.z);
                F(hz, hx, hy, size.x, size.y); F(-hz, hx, hy, size.x, size.y);
                F(hx, hz, hy, size.z, size.y); F(-hx, hz, hy, size.z, size.y);
            }

            public void Beam(int sub, Vector3 a, Vector3 b, float w, float h)
            {
                var f = b - a; float len = f.magnitude; if (len < 1e-4f) return;
                f /= len;
                var up = Mathf.Abs(Vector3.Dot(f, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
                var right = Vector3.Cross(up, f).normalized; up = Vector3.Cross(f, right);
                Box(sub, (a + b) * 0.5f, right, up, f, new Vector3(w, h, len));
            }
        }

        /// <summary>Box swept along the (curved) front edge: s ∈ [s0,s1], outward offset ∈ [o0,o1], height ∈ [y0,y1].</summary>
        void Strip(Builder b, int sub, float s0, float s1, float o0, float o1, float y0, float y1, bool bottom = false, float vShift = 0f)
        {
            if (s1 <= s0 || o1 <= o0 || y1 <= y0) return;
            int segs = Mathf.Abs(Kappa) < 1e-5f ? 1 : Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(Kappa) * (s1 - s0) / (4f * Mathf.Deg2Rad)), 1, 256);
            for (int i = 0; i < segs; i++)
            {
                float sa = Mathf.Lerp(s0, s1, i / (float)segs), sb = Mathf.Lerp(s0, s1, (i + 1) / (float)segs);
                Vector3 P(float s, float o, float y) => Point(s, o, y);
                float ua0 = (sa - s0) * Uv, ub0 = (sb - s0) * Uv;
                float uaF = ua0 * Stretch(o0), ubF = ub0 * Stretch(o0), uaB = ua0 * Stretch(o1), ubB = ub0 * Stretch(o1);
                var na = Outward(sa); var nb = Outward(sb); var nm = Outward((sa + sb) * 0.5f);
                // top
                b.Quad(sub, P(sa, o0, y1), P(sb, o0, y1), P(sb, o1, y1), P(sa, o1, y1),
                    new Vector2(uaF, vShift + o0 * Uv), new Vector2(ubF, vShift + o0 * Uv), new Vector2(ubB, vShift + o1 * Uv), new Vector2(uaB, vShift + o1 * Uv), Vector3.up);
                if (bottom)
                    b.Quad(sub, P(sa, o0, y0), P(sb, o0, y0), P(sb, o1, y0), P(sa, o1, y0),
                        new Vector2(uaF, o0 * Uv), new Vector2(ubF, o0 * Uv), new Vector2(ubB, o1 * Uv), new Vector2(uaB, o1 * Uv), Vector3.down);
                // front (faces the field) and back
                b.Quad(sub, P(sa, o0, y0), P(sb, o0, y0), P(sb, o0, y1), P(sa, o0, y1),
                    new Vector2(uaF, y0 * Uv), new Vector2(ubF, y0 * Uv), new Vector2(ubF, y1 * Uv), new Vector2(uaF, y1 * Uv), -nm);
                b.Quad(sub, P(sa, o1, y0), P(sb, o1, y0), P(sb, o1, y1), P(sa, o1, y1),
                    new Vector2(uaB, y0 * Uv), new Vector2(ubB, y0 * Uv), new Vector2(ubB, y1 * Uv), new Vector2(uaB, y1 * Uv), nm);
            }
            // end caps
            var t0 = Tangent(s0); var t1 = Tangent(s1);
            b.Quad(sub, Point(s0, o0, y0), Point(s0, o1, y0), Point(s0, o1, y1), Point(s0, o0, y1),
                new Vector2(o0 * Uv, y0 * Uv), new Vector2(o1 * Uv, y0 * Uv), new Vector2(o1 * Uv, y1 * Uv), new Vector2(o0 * Uv, y1 * Uv), -t0);
            b.Quad(sub, Point(s1, o0, y0), Point(s1, o1, y0), Point(s1, o1, y1), Point(s1, o0, y1),
                new Vector2(o0 * Uv, y0 * Uv), new Vector2(o1 * Uv, y0 * Uv), new Vector2(o1 * Uv, y1 * Uv), new Vector2(o0 * Uv, y1 * Uv), t1);
        }

        /// <summary>Wood plank strip: grain runs along the row, texture V stays inside one plank of the wood texture.</summary>
        void Plank(Builder b, int sub, float s0, float s1, float o0, float o1, float y0, float y1, int index)
        {
            float vShift = sub == SubWood ? ((index * 7) % 4) * 0.125f + 0.01f - o0 * Uv : 0f;
            Strip(b, sub, s0, s1, o0, o1, y0, y1, true, vShift);
        }

        /// <summary>Box placed at front-edge parameter s, offset o, height y, aligned to the stand (x = tangent, z = outward).</summary>
        void LocalBox(Builder b, int sub, float s, float o, float y, Vector3 size)
        {
            var t = Tangent(s); var nrm = Outward(s);
            b.Box(sub, Point(s, o, y), t, Vector3.up, nrm, size);
        }

        void BuildMesh()
        {
            var b = new Builder();
            float L = Length, half = L * 0.5f, d = Depth, rise = Rise;
            float sIn = half - Aisle - EndMargin;          // seating region s ∈ [-sIn, sIn]
            float topO = TotalDepth + (style == GrandstandStyle.Classic ? 0.15f : 0f);   // ledge behind the top row of seats
            int plankIx = 0;
            int deckSub = style == GrandstandStyle.Classic && deck == GrandstandDeck.Wood ? SubWood : SubConcrete;

            switch (style)
            {
                case GrandstandStyle.Classic:
                case GrandstandStyle.ConcreteSlabs:
                {
                    for (int r = 0; r < rows; r++)
                        Strip(b, deckSub, -half, half, RowOffset(r), r == rows - 1 ? topO : RowOffset(r) + d, 0f, RowHeight(r), false, deckSub == SubWood ? 0.01f : 0f);
                    if (aisleStairs)
                    {
                        // half-height step on the back half of every tread (and in front of row 0) at both ends
                        foreach (float side in new[] { -1f, 1f })
                        {
                            float a0 = side < 0 ? -half : sIn + EndMargin, a1 = side < 0 ? -sIn - EndMargin : half;
                            Strip(b, deckSub, a0, a1, -d * 0.5f, 0f, 0f, RowHeight(0) * 0.5f);
                            for (int r = 1; r < rows; r++)
                                Strip(b, deckSub, a0, a1, RowOffset(r) - d * 0.5f, RowOffset(r), RowHeight(r - 1), RowHeight(r - 1) + rise * 0.5f);
                        }
                    }
                    if (style == GrandstandStyle.Classic)
                        ForEachSeat((r, sp) => BucketSeat(b, sp));
                    break;
                }
                default:
                {
                    bool wooden = style == GrandstandStyle.WoodenBleacher;
                    int plankSub = wooden ? SubWood : SubAluminium;
                    const float plankW = 0.14f, gap = 0.015f, thick = 0.035f;
                    for (int r = 0; r < rows; r++)
                    {
                        float o = RowOffset(r), h = RowHeight(r);
                        // foot boards
                        float footEnd = o + d - 0.42f;
                        for (float p = o; p + plankW <= footEnd + 1e-3f; p += plankW + gap)
                            Plank(b, plankSub, -half, half, p, p + plankW, h - thick, h, plankIx++);
                        // seat boards (only across the seating region; aisles keep foot boards + half steps)
                        float seatTop = h + SeatHeight;
                        Plank(b, plankSub, -sIn - EndMargin, sIn + EndMargin, o + d - 0.38f, o + d - 0.38f + plankW, seatTop - thick, seatTop, plankIx++);
                        Plank(b, plankSub, -sIn - EndMargin, sIn + EndMargin, o + d - 0.38f + plankW + gap, o + d - 0.38f + 2 * plankW + gap, seatTop - thick, seatTop, plankIx++);
                        if (wooden)
                        {
                            // slatted riser below the seat front: three horizontal boards
                            float rf = o + d - 0.40f;
                            float slat = (SeatHeight - thick - 0.03f) / 3f;
                            for (int k = 0; k < 3; k++)
                                Plank(b, SubWood, -sIn - EndMargin, sIn + EndMargin, rf, rf + 0.02f, h + 0.01f + k * slat + 0.006f, h + 0.01f + (k + 1) * slat - 0.006f, plankIx++);
                        }
                        if (aisleStairs)
                            foreach (float side in new[] { -1f, 1f })
                            {
                                float a0 = side < 0 ? -half : sIn + EndMargin, a1 = side < 0 ? -sIn - EndMargin : half;
                                if (r < rows - 1)
                                    Plank(b, plankSub, a0, a1, o + d - 0.42f, o + d - 0.42f + 2 * plankW + gap, h + rise * 0.5f - thick, h + rise * 0.5f, plankIx++);
                            }
                    }
                    if (aisleStairs) Plank(b, plankSub, -half, -sIn - EndMargin, -0.35f, -0.05f, RowHeight(0) * 0.5f - 0.035f, RowHeight(0) * 0.5f, plankIx++);
                    if (aisleStairs) Plank(b, plankSub, sIn + EndMargin, half, -0.35f, -0.05f, RowHeight(0) * 0.5f - 0.035f, RowHeight(0) * 0.5f, plankIx++);
                    SteelFrame(b, half);
                    break;
                }
            }

            if (railings) Railings(b, half, topO);

            if (_mesh == null) _mesh = new Mesh { name = "Grandstand", hideFlags = HideFlags.DontSave };
            _mesh.Clear();
            _mesh.indexFormat = b.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            _mesh.SetVertices(b.v); _mesh.SetNormals(b.n); _mesh.SetUVs(0, b.uv);
            _mesh.subMeshCount = b.t.Length;
            for (int i = 0; i < b.t.Length; i++) _mesh.SetTriangles(b.t[i], i, false);
            _mesh.RecalculateBounds();
            _mesh.RecalculateTangents();

            var mf = GetComponent<MeshFilter>(); if (!mf) mf = gameObject.AddComponent<MeshFilter>();
            var mr = GetComponent<MeshRenderer>(); if (!mr) mr = gameObject.AddComponent<MeshRenderer>();
            mf.hideFlags = HideFlags.HideInInspector; mr.hideFlags = HideFlags.HideInInspector;
            mf.sharedMesh = _mesh;
            mr.sharedMaterials = new[] { concreteMaterial, woodMaterial, steelMaterial, aluminiumMaterial, seatMaterial };
            mr.shadowCastingMode = ShadowCastingMode.On;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", seatColor);
            mr.SetPropertyBlock(mpb, SubSeat);
        }

        void BucketSeat(Builder b, SeatSpot sp)
        {
            var nrm = Outward(sp.s); var fwd = -nrm; var right = Tangent(sp.s);
            var root = Point(sp.s, sp.o, sp.y);
            float w = Mathf.Min(0.46f, seatSpacing - 0.06f);
            // pan, slightly tilted back
            var panUp = Quaternion.AngleAxis(-4f, right) * Vector3.up; var panF = Vector3.Cross(right, panUp);
            b.Box(SubSeat, root - fwd * 0.05f + Vector3.up * (SeatHeight - 0.03f), right, panUp, panF, new Vector3(w, 0.05f, 0.42f));
            // backrest, reclined
            var backUp = Quaternion.AngleAxis(-12f, right) * Vector3.up; var backF = Vector3.Cross(right, backUp);
            b.Box(SubSeat, root - fwd * 0.27f + Vector3.up * (SeatHeight + 0.21f), right, backUp, backF, new Vector3(w * 0.96f, 0.42f, 0.04f));
            // pedestal (steel)
            b.Box(SubSteel, root - fwd * 0.12f + Vector3.up * ((SeatHeight - 0.05f) * 0.5f), right, Vector3.up, fwd, new Vector3(0.06f, SeatHeight - 0.05f, 0.06f));
        }

        void SteelFrame(Builder b, float half)
        {
            int frames = Mathf.Max(2, Mathf.CeilToInt(Length / 2.4f) + 1);
            float d = Depth;
            for (int i = 0; i < frames; i++)
            {
                float s = Mathf.Lerp(-half + 0.08f, half - 0.08f, i / (float)(frames - 1));
                // stringer from the ground in front of row 0 up to the back of the top row
                b.Beam(SubSteel, Point(s, 0.05f, 0f), Point(s, TotalDepth - 0.05f, RowHeight(rows - 1) - 0.04f), 0.07f, 0.12f);
                for (int r = 0; r < rows; r++)
                {
                    float o = RowOffset(r), h = RowHeight(r);
                    // joist under the foot boards / seat
                    b.Beam(SubSteel, Point(s, o, h - 0.06f), Point(s, o + d, h - 0.06f), 0.05f, 0.05f);
                    // seat bracket and leg
                    b.Beam(SubSteel, Point(s, o + d - 0.3f, h - 0.06f), Point(s, o + d - 0.3f, h + SeatHeight - 0.035f), 0.04f, 0.04f);
                    b.Beam(SubSteel, Point(s, o + d - 0.04f, 0f), Point(s, o + d - 0.04f, h - 0.06f), 0.05f, 0.05f);
                }
            }
        }

        float SurfaceHeight(int r) => RowHeight(r);

        void Railings(Builder b, float half, float topO)
        {
            const float bar = 0.025f, rail = 0.05f, baySpacing = 0.18f;
            // ---- back railing along the top row
            float baseY = Bench ? RowHeight(rows - 1) + SeatHeight : RowHeight(rows - 1);
            float backO = topO - 0.04f;
            float s0 = -half + 0.03f, s1 = half - 0.03f;
            Strip(b, SubSteel, s0, s1, backO - rail * 0.5f, backO + rail * 0.5f, baseY + RailHeight - rail, baseY + RailHeight, true);
            Strip(b, SubSteel, s0, s1, backO - rail * 0.5f, backO + rail * 0.5f, baseY + 0.08f, baseY + 0.08f + rail, true);
            float backLen = (s1 - s0) * Stretch(backO);
            int nb = Mathf.Max(2, Mathf.CeilToInt(backLen / baySpacing));
            for (int i = 0; i <= nb; i++)
            {
                float s = Mathf.Lerp(s0, s1, i / (float)nb);
                bool post = i % 10 == 0 || i == nb;
                float w = post ? 0.05f : bar;
                float y0 = post && Bench ? 0f : baseY;      // bleacher posts run down to the ground
                LocalBox(b, SubSteel, s, backO, (y0 + baseY + RailHeight) * 0.5f, new Vector3(w, baseY + RailHeight - y0, w));
            }
            // ---- side railings, following the steps
            foreach (float sideS in new[] { -half + 0.03f, half - 0.03f })
            {
                if (HasNeighbourAt(Point(sideS < 0 ? -half : half, 0f))) continue;   // the neighbouring stand continues here
                int n = Mathf.Max(2, Mathf.CeilToInt(topO / baySpacing));
                Vector3 prevTop = Vector3.zero;
                for (int i = 0; i <= n; i++)
                {
                    float o = Mathf.Lerp(0.02f, topO - 0.04f, i / (float)n);
                    int r = Mathf.Clamp(Mathf.FloorToInt(o / Depth), 0, rows - 1);
                    float y = SurfaceHeight(r);
                    bool post = i % 6 == 0 || i == n;
                    float w = post ? 0.05f : bar;
                    float y0 = Bench ? (post ? 0f : y) : y;
                    LocalBox(b, SubSteel, sideS, o, (y0 + y + RailHeight) * 0.5f, new Vector3(w, y + RailHeight - y0, w));
                    var top = Point(sideS, o, y + RailHeight);
                    if (i > 0) b.Beam(SubSteel, prevTop, top, rail, rail);
                    prevTop = top;
                }
            }
        }

        /// <summary>Places this stand directly after <paramref name="prev"/> (continuing its front edge and direction).</summary>
        public void PlaceAfter(Grandstand prev, float gap = 0.05f)
        {
            float hp = prev.Length * 0.5f;
            Vector3 endW = prev.transform.TransformPoint(prev.Point(hp, 0f));
            Vector3 tW = prev.transform.TransformDirection(prev.Tangent(hp)); tW.y = 0; tW.Normalize();
            float h = Length * 0.5f;
            Vector3 startL = Point(-h, 0f), tL = Tangent(-h);
            var rot = Quaternion.Euler(0f, Vector3.SignedAngle(tL, tW, Vector3.up), 0f);
            transform.SetPositionAndRotation(endW + tW * gap - rot * startL, rot);
        }

        /// <summary>Copies the user-facing settings (not the transform) from another stand.</summary>
        public void CopySettings(Grandstand o)
        {
            rows = o.rows; columns = o.columns; curve = o.curve; style = o.style; deck = o.deck; railings = o.railings;
            aisleStairs = o.aisleStairs; seatColor = o.seatColor; seatSpacing = o.seatSpacing; autoRowSize = o.autoRowSize;
            rowDepth = o.rowDepth; rowRise = o.rowRise; density = o.density; team = o.team; standingBias = o.standingBias;
            concreteMaterial = o.concreteMaterial; woodMaterial = o.woodMaterial; steelMaterial = o.steelMaterial;
            aluminiumMaterial = o.aluminiumMaterial; seatMaterial = o.seatMaterial;
            _dirty = true;
        }

        /// <summary>True when another grandstand under the same parent has a front-edge end at this local point.</summary>
        bool HasNeighbourAt(Vector3 local)
        {
            if (!transform.parent) return false;
            var w = transform.TransformPoint(local);
            foreach (var g in transform.parent.GetComponentsInChildren<Grandstand>())
            {
                if (g == this || g.transform.parent != transform.parent || !g.isActiveAndEnabled) continue;
                float h = g.Length * 0.5f;
                if ((g.transform.TransformPoint(g.Point(-h, 0f)) - w).sqrMagnitude < 0.09f || (g.transform.TransformPoint(g.Point(h, 0f)) - w).sqrMagnitude < 0.09f) return true;
            }
            return false;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.6f, 0f, 0.9f);
            float half = Length * 0.5f;
            Vector3 prev = Point(-half, 0f);
            for (int i = 1; i <= 32; i++) { var p = Point(Mathf.Lerp(-half, half, i / 32f), 0f); Gizmos.DrawLine(prev, p); prev = p; }
        }
    }
}
