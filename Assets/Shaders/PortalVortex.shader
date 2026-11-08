Shader "Custom/URP/PortalTwinProcedural"
{
    Properties
    {
        _MainTex ("Imagem do Destino", 2D) = "white" {}
        _TextureIntensity ("Intensidade Imagem", Range(0, 3)) = 2.0
        _TextureBrightness ("Brilho Imagem", Range(0, 3)) = 1.5
        _TextureDistortion ("Distorção da Imagem", Range(0, 1)) = 0.15
        
        _RotationSpeed ("Velocidade Rotação (Borda)", Float) = 50.0
        _TwinOffset ("Offset Twin", Range(0, 1)) = 0.5
        _ColorInner ("Cor Borda Interior", Color) = (0.9, 0.95, 1.0, 1)
        _ColorMiddle ("Cor Borda Meio", Color) = (0.2, 0.6, 1.0, 1)
        _ColorOuter ("Cor Borda Exterior", Color) = (0.0, 0.1, 0.3, 1)
        _SpiralDensity ("Densidade Espiral", Range(1, 20)) = 8.0
        _GlowIntensity ("Intensidade Glow", Range(0, 5)) = 2.5
        _Transparency ("Transparência", Range(0, 1)) = 0.9
        _TwistAmount ("Força da Distorção", Range(0, 5)) = 2.0
        _PortalEdgeWidth ("Largura Borda Portal", Range(0.1, 1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 200
        
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "PortalTwin"
            Tags { "LightMode"="UniversalForward" }
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _RotationSpeed;
                float _TwinOffset;
                half4 _ColorInner;
                half4 _ColorMiddle;
                half4 _ColorOuter;
                float _SpiralDensity;
                float _GlowIntensity;
                float _Transparency;
                float _TwistAmount;
                float _TextureIntensity;
                float _TextureBrightness;
                float _TextureDistortion;
                float _PortalEdgeWidth;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            // Função que aplica APENAS distorção radial, SEM rotação
            float2 GetDistortedStaticUV(float2 uv, float distortionAmount)
            {
                if (distortionAmount <= 0.001) 
                    return uv; // Sem distorção, UV original

                float2 centeredUV = uv - 0.5;
                float angle = atan2(centeredUV.y, centeredUV.x);
                float radius = length(centeredUV);
                
                // AQUI ESTÁ O SEGREDO: Não somamos nenhuma rotação ao 'angle'
                // Apenas aplicamos o 'twist' baseado no raio e no slider
                float twist = radius * _TwistAmount * 4.0 * distortionAmount;
                angle += twist;
                
                // Converte de volta para coordenadas Cartesianas (X, Y)
                float2 distorted;
                distorted.x = cos(angle) * radius;
                distorted.y = sin(angle) * radius;
                distorted += 0.5;
                
                return distorted;
            }

            // Gera apenas a borda do vortex (que essa sim, gira)
            half3 GenerateVortexEdge(float2 uv, float rotation, float twinOffset, float time, float radius)
            {
                float angle = atan2(uv.y - 0.5, uv.x - 0.5);
                angle += rotation; // A borda GIRA normalmente
                
                float spiral = angle * _SpiralDensity + radius * _TwistAmount * 10.0;
                float pattern = sin(spiral + time * _RotationSpeed * 0.5);
                float twinPattern = sin(spiral + twinOffset * 6.28 + time * _RotationSpeed * 0.3);
                
                float combinedPattern = (pattern + twinPattern) * 0.5;
                combinedPattern = combinedPattern * 0.5 + 0.5;
                
                // Cria máscara de borda: só aparece nas extremidades
                float edgeMask = smoothstep(0.5, 0.5 - _PortalEdgeWidth * 0.5, radius);
                edgeMask *= smoothstep(0.5 - _PortalEdgeWidth, 0.5 - _PortalEdgeWidth * 0.5, radius);
                
                float radialGradient = 1.0 - smoothstep(0.0, 0.5, radius);
                
                half3 color = lerp(_ColorOuter.rgb, _ColorMiddle.rgb, combinedPattern);
                color = lerp(color, _ColorInner.rgb, radialGradient * combinedPattern);
                color += radialGradient * _ColorInner.rgb * 0.5;
                
                return color * edgeMask;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float time = _Time.y;
                float2 centeredUV = input.uv - 0.5;
                float radius = length(centeredUV);
                
                // === ROTAÇÕES (APENAS PARA A BORDA) ===
                float rotation1 = time * _RotationSpeed * 0.02;
                float rotation2 = time * _RotationSpeed * 0.015 + _TwinOffset * 6.28;
                
                // === IMAGEM DO DESTINO (CENTRO) ===
                // Passamos 0.0 como rotação, mas mantemos o _TextureDistortion
                float2 texUV = GetDistortedStaticUV(input.uv, _TextureDistortion);
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, texUV);
                
                // Aumenta brilho e intensidade para garantir legibilidade
                texColor.rgb *= _TextureBrightness;
                texColor.rgb = lerp(half3(0.5, 0.5, 0.5), texColor.rgb, _TextureIntensity);
                
                // === EFEITO VORTEX (BORDA) ===
                half3 vortexEdge1 = GenerateVortexEdge(input.uv, rotation1, 0.0, time, radius);
                half3 vortexEdge2 = GenerateVortexEdge(input.uv, rotation2, 0.5, time, radius);
                half3 vortexEdge = (vortexEdge1 + vortexEdge2) * 0.5;
                
                // === COMPOSIÇÃO FINAL ===
                // Imagem no centro, vortex na borda
                float centerMask = 1.0 - smoothstep(0.35, 0.5, radius); 
                float edgeMask = smoothstep(0.35, 0.5, radius); 
                
                half3 finalColor = lerp(vortexEdge, texColor.rgb, centerMask);
                
                // Adiciona glow na borda
                finalColor += vortexEdge * _GlowIntensity * 0.3;
                
                // Alpha baseado na luminosidade
                half alpha = dot(finalColor, half3(0.3, 0.59, 0.11)) * _Transparency;
                alpha = saturate(alpha);
                alpha = max(alpha, 0.5 * _Transparency); // Garante que não fique invisível
                
                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}