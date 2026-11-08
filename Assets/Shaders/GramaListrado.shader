Shader "Custom/URP/GramaListrado"
{
    Properties
    {
        _ColorDark ("Cor Escura", Color) = (0.15, 0.35, 0.10, 1)
        _ColorLight ("Cor Clara", Color) = (0.25, 0.50, 0.15, 1)
        _Stripes ("Listras", Float) = 9.0
        _Threshold ("Threshold", Range(0,1)) = 0.5
        _Roughness ("Roughness", Range(0,1)) = 0.9
        _MainTex ("Textura", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 200

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorDark;
                half4 _ColorLight;
                float _Stripes;
                float _Threshold;
                float _Roughness;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = posInput.positionCS;
                output.positionWS = posInput.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // === LÓGICA DAS LISTRAS ===
                float posX = input.positionWS.x;
                float multiplied = posX * _Stripes;
                float fractioned = frac(multiplied);
                float mask = step(_Threshold, fractioned);
                
                half3 baseColor = lerp(_ColorDark.rgb, _ColorLight.rgb, mask);
                
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                baseColor *= tex.rgb;
                
                // Iluminação URP
                Light mainLight = GetMainLight();
                float NdotL = max(0, dot(input.normalWS, mainLight.direction));
                half3 diffuse = baseColor * mainLight.color * NdotL;
                half3 ambient = baseColor * half3(0.1, 0.1, 0.1);
                
                return half4(diffuse + ambient, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}