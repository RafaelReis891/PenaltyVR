// GPU-skinned, indirectly-instanced crowd character (URP, Forward / Forward+).
// Every spectator is drawn from a shared mesh + Texture2DArray atlas; appearance comes from per-instance data.
Shader "AiKodex/Stadium Creator 2/Crowd Character"
{
    Properties
    {
        [NoScaleOffset] _AlbedoArray ("Albedo Atlas Array", 2DArray) = "" {}
        [NoScaleOffset] _NormalArray ("Normal Atlas Array", 2DArray) = "" {}
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.45
        _NormalStrength ("Normal Strength", Range(0, 2)) = 1
        _NormalGreenSign ("Normal Green Sign (-1 = DirectX maps)", Float) = 1
        _SkinSmoothness ("Skin Smoothness", Range(0, 1)) = 0.38
        _ClothSmoothness ("Cloth Smoothness", Range(0, 1)) = 0.18
        _KitStrength ("Team Kit Tint Strength", Range(0, 1)) = 1
        _Saturation ("Albedo Saturation", Range(0, 2)) = 1
        [HideInInspector] _BoneCountF ("Bone Count", Float) = 52
        _WrapLighting ("Wrap Lighting (soft skin/cloth)", Range(0, 1)) = 0.25
        [Toggle(_CROWD_NORMALMAP)] _UseNormalMap ("Use Normal Map", Float) = 1
        [Toggle(_CROWD_ALPHATEST)] _AlphaTest ("Alpha Test (hair cards)", Float) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "CrowdCommon.hlsl"

    #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
    #include "UnityIndirect.cginc"

    TEXTURE2D_ARRAY(_AlbedoArray); SAMPLER(sampler_AlbedoArray);
    TEXTURE2D_ARRAY(_NormalArray); SAMPLER(sampler_NormalArray);

    CBUFFER_START(UnityPerMaterial)
        float _Cutoff;
        float _NormalStrength;
        float _NormalGreenSign;
        float _SkinSmoothness;
        float _ClothSmoothness;
        float _KitStrength;
        float _Saturation;
        float _WrapLighting;
        float _UseNormalMap;
        float _BoneCountF;      // bones per skeleton (float: Material.SetFloat is the only reliable per-material scalar path)
    CBUFFER_END

    StructuredBuffer<CrowdInstance> _Instances;
    StructuredBuffer<uint>          _DrawList;
    StructuredBuffer<float4>        _Palette;
    StructuredBuffer<float4>        _VariantInfo;  // x top mean lum, y bottom mean lum, z hair mean lum

    struct Attributes
    {
        float3 positionOS : POSITION;
        float3 normalOS   : NORMAL;
        float4 tangentOS  : TANGENT;
        float2 uv         : TEXCOORD0;
        float2 part       : TEXCOORD1;
        float4 boneIdx    : TEXCOORD2;
        float4 boneW      : TEXCOORD3;
        uint   instanceID : SV_InstanceID;
    };

    struct SkinnedVertex
    {
        float3 positionWS;
        float3 normalWS;
        float4 tangentWS;
        float3 bindPos;
        uint   instance;
    };

    SkinnedVertex CrowdSkin(Attributes v)
    {
        InitIndirectDrawArgs(0);
        uint drawIndex = GetIndirectInstanceID_Base(v.instanceID);
        uint id = _DrawList[drawIndex];
        CrowdInstance inst = _Instances[id];

        float3 p = 0, n = 0, t = 0;
        float wsum = 0;
        uint baseIdx = id * (uint)(_BoneCountF + 0.5);
        [unroll] for (uint k = 0; k < 4; k++)
        {
            float w = v.boneW[k];
            if (w > 0)
            {
                uint o = (baseIdx + (uint)(v.boneIdx[k] + 0.5)) * 2;
                float4 q = _Palette[o];
                float3 tr = _Palette[o + 1].xyz;
                p += w * (CrowdQRot(q, v.positionOS) + tr);
                n += w * CrowdQRot(q, v.normalOS);
                t += w * CrowdQRot(q, v.tangentOS.xyz);
                wsum += w;
            }
        }
        p /= max(wsum, 1e-4);

        float s, c;
        sincos(inst.yaw, s, c);
        float3x3 rotY = float3x3(c, 0, s, 0, 1, 0, -s, 0, c);

        SkinnedVertex o;
        o.positionWS = inst.position + mul(rotY, p * inst.scale);
        o.normalWS = CrowdSafeNormalize(mul(rotY, n), float3(0, 1, 0));
        o.tangentWS = float4(CrowdSafeNormalize(mul(rotY, t), float3(1, 0, 0)), v.tangentOS.w);
        o.bindPos = v.positionOS;
        o.instance = id;
        return o;
    }

    float KitPatternMask(uint pattern, float3 bp)
    {
        // bind-pose character space: +Y up (feet at 0), +Z forward, X across
        if (pattern == 1) return step(0.5, frac(bp.x * 7.0 + 0.25));                    // vertical stripes
        if (pattern == 2) return step(0.5, frac(bp.y * 5.5));                            // hoops
        if (pattern == 3) return step(0.235, abs(bp.x)) * step(abs(bp.x), 0.6);          // sleeves
        if (pattern == 4) return step(abs(bp.y - 1.24), 0.055);                          // chest band
        if (pattern == 5) return step(0.0, bp.x);                                        // halves
        return 0;
    }

    float SampleAlpha(float2 uv, uint variant)
    {
        return SAMPLE_TEXTURE2D_ARRAY(_AlbedoArray, sampler_AlbedoArray, uv, variant).a;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ _CROWD_ALPHATEST
            #pragma multi_compile_local _ _CROWD_NORMALMAP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 tangentWS  : TEXCOORD2;
                float2 uv         : TEXCOORD3;
                float3 bindPos    : TEXCOORD4;
                nointerpolation uint2 data : TEXCOORD5; // instance, part
                float fog         : TEXCOORD6;
            };

            Varyings Vert(Attributes v)
            {
                SkinnedVertex s = CrowdSkin(v);
                Varyings o;
                o.positionWS = s.positionWS;
                o.positionCS = TransformWorldToHClip(s.positionWS);
                o.normalWS = s.normalWS;
                o.tangentWS = s.tangentWS;
                o.uv = v.uv;
                o.bindPos = s.bindPos;
                o.data = uint2(s.instance, (uint)(v.part.x + 0.5));
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float3 Tint(float3 albedo, float3 tintLinear, float meanLum, float strength)
            {
                float lum = dot(albedo, float3(0.2126, 0.7152, 0.0722));
                float shade = lum / max(meanLum, 0.02);
                shade = shade / (1 + 0.25 * max(shade - 1, 0)); // soften highlights
                return lerp(albedo, tintLinear * shade, strength);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                CrowdInstance inst = _Instances[i.data.x];
                uint part = i.data.y;
                float4 alb = SAMPLE_TEXTURE2D_ARRAY(_AlbedoArray, sampler_AlbedoArray, i.uv, inst.variant);
                #if defined(_CROWD_ALPHATEST)
                    clip(alb.a - _Cutoff);
                #endif
                float3 albedo = alb.rgb;
                float4 vinfo = _VariantInfo[inst.variant];
                float smooth = _ClothSmoothness;
                float wrap = _WrapLighting;

                // normal atlas: RG = tangent normal XY, B = upper-garment mask, A = lower-garment mask
                float4 nm = SAMPLE_TEXTURE2D_ARRAY(_NormalArray, sampler_NormalArray, i.uv, inst.variant);
                float tone = lerp(0.88, 1.1, ((inst.packed >> 8) & 255u) / 255.0);
                if (part == 0 || part == 10)
                {
                    // body texture = skin + clothes; garment masks select what may be recoloured
                    float topM = part == 0 ? nm.b : 0;
                    float botM = part == 0 ? nm.a : 0;
                    float skinM = saturate(1 - topM - botM);
                    smooth = lerp(_ClothSmoothness, _SkinSmoothness, skinM);
                    float4 c1 = CrowdUnpackColor(inst.colorTop);
                    float4 c2 = CrowdUnpackColor(inst.colorTop2);
                    float pm = KitPatternMask(inst.packed & 255u, i.bindPos);
                    float3 kit = SRGBToLinear(lerp(c1.rgb, c2.rgb, pm));
                    float3 a0 = albedo;
                    albedo = lerp(albedo, Tint(a0, kit, vinfo.x, 1), topM * c1.a * _KitStrength);
                    float4 cb = CrowdUnpackColor(inst.colorBottom);
                    albedo = lerp(albedo, Tint(a0, SRGBToLinear(cb.rgb), vinfo.y, 1), botM * cb.a);
                    albedo *= lerp(1, tone, skinM);
                }
                else if (part == 4)
                {
                    // hair / eyelashes cards: light tint only, original strands keep their colour variation
                    float4 ch = CrowdUnpackColor(inst.colorHair);
                    albedo = Tint(albedo, SRGBToLinear(ch.rgb), vinfo.z, ch.a);
                    smooth = 0.35;
                    wrap = 0.5;
                }

                float lumA = dot(albedo, float3(0.2126, 0.7152, 0.0722));
                albedo = max(lerp(lumA.xxx, albedo, _Saturation), 0);

                float3 n = CrowdSafeNormalize(i.normalWS, float3(0, 1, 0));
                #if defined(_CROWD_NORMALMAP)
                    float2 txy = (nm.rg * 2 - 1);
                    txy.y *= _NormalGreenSign;
                    txy *= _NormalStrength;
                    float tz = sqrt(saturate(1 - dot(txy, txy)));
                    float3 tg = CrowdSafeNormalize(i.tangentWS.xyz - n * dot(n, i.tangentWS.xyz), abs(n.y) < 0.9 ? normalize(cross(float3(0, 1, 0), n)) : float3(1, 0, 0));
                    float3 bt = cross(n, tg) * i.tangentWS.w;
                    n = CrowdSafeNormalize(txy.x * tg + txy.y * bt + max(tz, 0.1) * n, n);
                #endif

                InputData input = (InputData)0;
                input.positionWS = i.positionWS;
                input.positionCS = i.positionCS;
                input.normalWS = n;
                input.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                input.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                input.fogCoord = i.fog;
                input.bakedGI = SampleSH(n);
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                input.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surf = (SurfaceData)0;
                surf.albedo = albedo;
                surf.metallic = 0;
                surf.specular = 0;
                surf.smoothness = smooth;
                surf.occlusion = 1;
                surf.alpha = 1;
                surf.normalTS = float3(0, 0, 1);

                half4 col = UniversalFragmentPBR(input, surf);
                // cheap wrap / back-scatter so faces and cloth do not go pitch black on the shadow side
                Light mainLight = GetMainLight(input.shadowCoord);
                float ndl = dot(n, mainLight.direction);
                float wrapTerm = saturate((ndl + wrap) / (1 + wrap)) - saturate(ndl);
                col.rgb += albedo * mainLight.color * wrapTerm * 0.5 * mainLight.shadowAttenuation;
                col.rgb = MixFog(col.rgb, i.fog);
                // never emit NaN/Inf: one bad pixel is blown up into a large disc by bloom
                col.rgb = any(isnan(col.rgb)) || any(isinf(col.rgb)) ? albedo * 0.3 : min(col.rgb, 64.0);
                return half4(col.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ _CROWD_ALPHATEST
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation uint variant : TEXCOORD1;
            };

            Varyings Vert(Attributes v)
            {
                SkinnedVertex s = CrowdSkin(v);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - s.positionWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                Varyings o;
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(s.positionWS, s.normalWS, lightDir));
                #if UNITY_REVERSED_Z
                    o.positionCS.z = min(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    o.positionCS.z = max(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.uv = v.uv;
                o.variant = _Instances[s.instance].variant;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                #if defined(_CROWD_ALPHATEST)
                    clip(SampleAlpha(i.uv, i.variant) - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ _CROWD_ALPHATEST

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation uint variant : TEXCOORD1;
            };

            Varyings Vert(Attributes v)
            {
                SkinnedVertex s = CrowdSkin(v);
                Varyings o;
                o.positionCS = TransformWorldToHClip(s.positionWS);
                o.uv = v.uv;
                o.variant = _Instances[s.instance].variant;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                #if defined(_CROWD_ALPHATEST)
                    clip(SampleAlpha(i.uv, i.variant) - _Cutoff);
                #endif
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ _CROWD_ALPHATEST
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                nointerpolation uint variant : TEXCOORD2;
            };

            Varyings Vert(Attributes v)
            {
                SkinnedVertex s = CrowdSkin(v);
                Varyings o;
                o.positionCS = TransformWorldToHClip(s.positionWS);
                o.uv = v.uv;
                o.normalWS = s.normalWS;
                o.variant = _Instances[s.instance].variant;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                #if defined(_CROWD_ALPHATEST)
                    clip(SampleAlpha(i.uv, i.variant) - _Cutoff);
                #endif
                float3 n = CrowdSafeNormalize(i.normalWS, float3(0, 1, 0));
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(n);
                    return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0);
                #else
                    return half4(n, 0);
                #endif
            }
            ENDHLSL
        }
    }
}
