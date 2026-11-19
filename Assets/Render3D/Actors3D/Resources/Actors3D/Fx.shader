// Actors3D/Fx: unlit, GPU-instanced effect quads for the 2.5D scene (sparks, debris, cut flash, rain spray,
// splash rings, blast flash, smoke, embers, orb glows and tokens, screen flash).
// Per instance (MaterialPropertyBlock): _Color (HDR rgb, a = opacity) and _Params:
//   x = shape  0 solid quad, 1 soft radial glow, 2 thin ring, 3 texture (_MainTex, point sampled)
//   y = fog amount (0 ignores scene fog, 1 full: additive fades to black, alpha-blend fades to the fog colour)
// Blend factors come from the material (_SrcBlend/_DstBlend): additive (One, One) or alpha (SrcAlpha, OneMinusSrcAlpha).
// Additive output is colour * alpha, so fading alpha fades additive effects too.
Shader "Actors3D/Fx"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (1,1,1,1)
        _Params ("Shape, Fog", Vector) = (0,1,0,0)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 1
        _ZTest ("ZTest", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Fx"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest [_ZTest]
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _SrcBlend; half _DstBlend; half _ZTest;
            CBUFFER_END
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Params)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float fogFactor : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 col = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                float4 prm = UNITY_ACCESS_INSTANCED_PROP(Props, _Params);
                float2 p = i.uv * 2.0 - 1.0;
                float r = length(p);
                half a = 1.0h;
                half3 rgb = col.rgb;
                if (prm.x > 2.5)
                {
                    half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv * _MainTex_ST.xy + _MainTex_ST.zw);
                    rgb *= t.rgb; a = t.a;
                }
                else if (prm.x > 1.5) a = 1.0h - saturate(abs(r - 0.86) / 0.11);
                else if (prm.x > 0.5) { half g = saturate(1.0 - r); a = g * g * (0.35h + 0.65h * g); }
                a *= col.a;
                if (a <= 0.002h) discard;
                bool additive = _DstBlend == 1;
                half3 fogged = MixFogColor(rgb, additive ? half3(0, 0, 0) : unity_FogColor.rgb, i.fogFactor);
                rgb = lerp(rgb, fogged, prm.y);
                return additive ? half4(rgb * a, a) : half4(rgb, a);
            }
            ENDHLSL
        }
    }
}
