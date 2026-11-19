Shader "Hidden/Ronin/Bake"
{
    Properties
    {
        _BaseMap ("Base", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1,1,1,1)
        _EmissionMap ("Emission mask", 2D) = "black" {}   // black: a material without a map bakes no glow (the baker sets _EmitFull where a part must glow whole)
        _Chan ("Emissive channel weights", Vector) = (0,0,0,0)
        _Mode ("0 albedo, 1 view normal, 2 emissive", Float) = 0
        _EmitFull ("Emit whole surface", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "Bake"
            Cull Off
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _Chan;
                float _Mode;
                float _EmitFull;
            CBUFFER_END
            struct A { float4 p : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 p : SV_POSITION; float3 n : TEXCOORD1; float2 uv : TEXCOORD0; };
            V vert(A i)
            {
                V o;
                o.p = TransformObjectToHClip(i.p.xyz);
                o.n = TransformWorldToViewDir(TransformObjectToWorldNormal(i.n), true);
                o.uv = i.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                return o;
            }
            float4 frag(V i, bool ff : SV_IsFrontFace) : SV_Target
            {
                if (_Mode < 0.5)
                {
                    float4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                    return float4(c.rgb, 1);
                }
                if (_Mode < 1.5)
                {
                    float3 n = normalize(i.n);
                    n = ff ? n : -n;
                    return float4(n * 0.5 + 0.5, 1);
                }
                float3 e = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb;
                float m = _EmitFull > 0.5 ? 1.0 : step(0.25, dot(e, float3(0.333, 0.333, 0.333)));
                return float4(_Chan.rgb * m, 1);
            }
            ENDHLSL
        }
    }
}
