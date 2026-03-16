Shader "Peribind/Procedural Wood"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (0.66, 0.52, 0.35, 1)
        _DarkColor("Dark Grain Color", Color) = (0.28, 0.18, 0.10, 1)
        _SpecularColor("Specular Color", Color) = (0.20, 0.17, 0.12, 1)
        _UVScale("UV Scale", Vector) = (3, 3, 0, 0)
        _UVOffset("UV Offset", Vector) = (0, 0, 0, 0)
        _GrainDirection("Grain Direction", Vector) = (0, 1, 0, 0)
        _GrainScale("Grain Scale", Range(1, 80)) = 28
        _GrainContrast("Grain Contrast", Range(0, 1)) = 0.65
        _WarpStrength("Warp Strength", Range(0, 1)) = 0.22
        _PoreScale("Pore Scale", Range(1, 120)) = 48
        _PoreStrength("Pore Strength", Range(0, 1)) = 0.18
        _NormalStrength("Normal Strength", Range(0, 2)) = 0.7
        _Smoothness("Smoothness", Range(0, 1)) = 0.3
        _SpecularStrength("Specular Strength", Range(0, 1)) = 0.12
        _AmbientStrength("Ambient Strength", Range(0, 1.5)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _DarkColor;
                half4 _SpecularColor;
                float4 _UVScale;
                float4 _UVOffset;
                float4 _GrainDirection;
                float _GrainScale;
                float _GrainContrast;
                float _WarpStrength;
                float _PoreScale;
                float _PoreStrength;
                float _NormalStrength;
                float _Smoothness;
                float _SpecularStrength;
                float _AmbientStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                half fogFactor : TEXCOORD4;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float Fbm(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.5;

                [unroll(4)]
                for (int octave = 0; octave < 4; octave++)
                {
                    value += ValueNoise(p) * amplitude;
                    p = p * 2.03 + 17.13;
                    amplitude *= 0.5;
                }

                return value;
            }

            float2 GetWoodCoords(float2 uv)
            {
                float2 scaledUv = uv * _UVScale.xy + _UVOffset.xy;
                float2 direction = _GrainDirection.xy;
                float directionLength = max(length(direction), 0.0001);
                direction /= directionLength;

                float2 across = float2(-direction.y, direction.x);
                return float2(dot(scaledUv, across), dot(scaledUv, direction));
            }

            float WoodHeight(float2 uv)
            {
                float2 woodUv = GetWoodCoords(uv);
                float warpNoise = Fbm(float2(woodUv.x * 1.6, woodUv.y * 0.25));
                float warp = (warpNoise - 0.5) * _WarpStrength;

                float grainNoise = Fbm(float2(woodUv.x * 0.8, woodUv.y * 0.12));
                float grain = sin((woodUv.y + warp + grainNoise * 0.15) * _GrainScale);
                grain = grain * 0.5 + 0.5;
                grain = pow(saturate(grain), lerp(1.5, 6.0, _GrainContrast));

                float bands = Fbm(float2(woodUv.x * 2.0, woodUv.y * 0.08));
                float pores = Fbm(float2(woodUv.x * _PoreScale * 0.08, woodUv.y * _PoreScale));
                pores = smoothstep(0.62, 0.86, pores) * _PoreStrength;

                return saturate(grain * 0.75 + bands * 0.25 - pores * 0.35);
            }

            float3 EvaluateWoodColor(float2 uv, out float height)
            {
                height = WoodHeight(uv);

                float2 woodUv = GetWoodCoords(uv);
                float variation = Fbm(float2(woodUv.x * 0.7, woodUv.y * 0.05));
                float colorMask = saturate(height * 0.85 + variation * 0.25);

                return lerp(_DarkColor.rgb, _BaseColor.rgb, colorMask);
            }

            float3 BuildWoodNormal(float2 uv, float3 baseNormalWS, float4 tangentWS)
            {
                float2 epsilon = max(float2(0.0005, 0.0005), 0.002 / max(_UVScale.xy, float2(0.0001, 0.0001)));
                float height = WoodHeight(uv);
                float heightX = WoodHeight(uv + float2(epsilon.x, 0.0));
                float heightY = WoodHeight(uv + float2(0.0, epsilon.y));

                float3 normalWS = normalize(baseNormalWS);
                float tangentLength = length(tangentWS.xyz);
                float tangentSign = tangentWS.w == 0.0 ? 1.0 : tangentWS.w;
                float3 tangentDir = tangentLength > 0.001
                    ? normalize(tangentWS.xyz)
                    : normalize(abs(normalWS.y) > 0.99 ? cross(normalWS, float3(1.0, 0.0, 0.0)) : cross(float3(0.0, 1.0, 0.0), normalWS));
                float3 bitangentDir = normalize(cross(normalWS, tangentDir)) * tangentSign;

                float3 tangentNormal = normalize(float3(
                    (height - heightX) * _NormalStrength,
                    (height - heightY) * _NormalStrength,
                    1.0));

                return normalize(
                    tangentNormal.x * tangentDir +
                    tangentNormal.y * bitangentDir +
                    tangentNormal.z * normalWS);
            }

            float3 EvaluateLightContribution(float3 albedo, float3 normalWS, float3 viewDirWS, float smoothness, Light light)
            {
                float3 lightDirWS = normalize(light.direction);
                float attenuation = light.distanceAttenuation * light.shadowAttenuation;
                float ndotl = saturate(dot(normalWS, lightDirWS));

                float3 diffuse = albedo * light.color * ndotl * attenuation;

                float3 halfDir = SafeNormalize(lightDirWS + viewDirWS);
                float specPower = lerp(8.0, 96.0, smoothness);
                float specularTerm = pow(saturate(dot(normalWS, halfDir)), specPower) * _SpecularStrength;
                float3 specular = _SpecularColor.rgb * light.color * specularTerm * ndotl * attenuation;

                return diffuse + specular;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = float4(normalInputs.tangentWS, input.tangentOS.w);
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float height;
                float3 albedo = EvaluateWoodColor(input.uv, height);
                float3 normalWS = BuildWoodNormal(input.uv, input.normalWS, input.tangentWS);
                float3 viewDirWS = SafeNormalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                float smoothness = saturate(_Smoothness + (height - 0.5) * 0.08);

                float3 color = SampleSH(normalWS) * albedo * _AmbientStrength;

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                color += EvaluateLightContribution(albedo, normalWS, viewDirWS, smoothness, mainLight);

                #if defined(_ADDITIONAL_LIGHTS)
                uint additionalLightsCount = GetAdditionalLightsCount();
                for (uint lightIndex = 0; lightIndex < additionalLightsCount; ++lightIndex)
                {
                    Light additionalLight = GetAdditionalLight(lightIndex, input.positionWS, half4(1.0, 1.0, 1.0, 1.0));
                    color += EvaluateLightContribution(albedo, normalWS, viewDirWS, smoothness, additionalLight);
                }
                #endif

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
        UsePass "Universal Render Pipeline/Lit/Meta"
    }
}
