Shader "Peribind/Skybox/Equirect3Axis"
{
    Properties
    {
        _MainTex ("Equirect Texture", 2D) = "white" {}
        _Rotation ("Rotation XYZ (Degrees)", Vector) = (0, 0, 0, 0)
        _Exposure ("Exposure", Range(0, 8)) = 1
        _Tint ("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
        }

        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Rotation;
            float _Exposure;
            float4 _Tint;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            float3x3 RotX(float a)
            {
                float s = sin(a);
                float c = cos(a);
                return float3x3(
                    1, 0, 0,
                    0, c, -s,
                    0, s, c
                );
            }

            float3x3 RotY(float a)
            {
                float s = sin(a);
                float c = cos(a);
                return float3x3(
                    c, 0, s,
                    0, 1, 0,
                    -s, 0, c
                );
            }

            float3x3 RotZ(float a)
            {
                float s = sin(a);
                float c = cos(a);
                return float3x3(
                    c, -s, 0,
                    s, c, 0,
                    0, 0, 1
                );
            }

            float2 DirToEquirectUV(float3 d)
            {
                d = normalize(d);
                float2 uv;
                uv.x = atan2(d.x, d.z) / (2.0 * UNITY_PI) + 0.5;
                uv.y = 0.5 - asin(clamp(d.y, -1.0, 1.0)) / UNITY_PI;
                return uv;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.dir);
                float3 rot = radians(_Rotation.xyz);

                // Apply X, then Y, then Z rotation.
                float3x3 R = mul(RotZ(rot.z), mul(RotY(rot.y), RotX(rot.x)));
                dir = mul(R, dir);

                float2 uv = DirToEquirectUV(dir);
                fixed4 col = tex2D(_MainTex, uv);
                col.rgb *= _Tint.rgb * _Exposure;
                return col;
            }
            ENDCG
        }
    }

    Fallback Off
}
