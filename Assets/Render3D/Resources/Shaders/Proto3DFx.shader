// Proto3D/Fx: unlit effect shader for the 2.5D render prototype (haze cards, light shafts, glows, rain,
// sky backdrop). Texture * _Color * vertex colour, blend factors from material properties
// (additive: One/One; alpha: SrcAlpha/OneMinusSrcAlpha; opaque: One/Zero with _ZWrite 1).
// _FogAmount: 0 = ignores fog, 1 = full scene fog (additive fades to black, alpha-blend toward fog colour).
// _EdgePow > 0 fades faces seen edge-on (soft cone edges for light shafts).
// _EmMask = 1 switches to the sprite emissive-mask mode: rgb mask channels tinted by _TintR/_TintG/_TintB.
// _Wet = 1 switches to the wet-reflection mode (no texture read): a quad lying on the deck, uv.x across and uv.y
// along its length (v = 0 under the light, 1 nearest the camera). Draws a narrow streak that widens and fades
// toward the camera, rippled sideways by _Ripple, broken into wave dashes, flickering per light. Vertex colour rgb
// tints each streak, vertex alpha seeds its flicker, so many streaks batch into one mesh. Additive. Pure ALU, ~30 instructions.
Shader "Proto3D/Fx"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (1,1,1,1)
        _FogAmount ("Fog Amount", Range(0,1)) = 1
        _EdgePow ("Edge Fade Power", Float) = 0
        _EmMask ("Emissive Mask Mode", Float) = 0
        _Wet ("Wet Reflection Mode", Float) = 0
        _Ripple ("Wet Ripple Amount", Float) = 0.12
        [HDR] _TintR ("Tint R", Color) = (1,0.2,0.25,1)
        [HDR] _TintG ("Tint G", Color) = (0.4,1,1,1)
        [HDR] _TintB ("Tint B", Color) = (0.4,1,1,1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 1
        _ZWrite ("ZWrite", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Fx"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _FogAmount;
                half _EdgePow;
                half _EmMask;
                half _Wet;
                half _Ripple;
                half _DstBlend;
                half4 _TintR; half4 _TintG; half4 _TintB;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; float fogFactor : TEXCOORD1; float3 normalWS : TEXCOORD2; float3 viewWS : TEXCOORD3; float seed : TEXCOORD4; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceViewDir(posWS);
                float3 originWS = TransformObjectToWorld(float3(0, 0, 0));
                o.seed = frac(sin(originWS.x * 12.9898 + originWS.y * 4.1414) * 43758.5453);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 c;
                if (_Wet > 0.5)
                {
                    float t = _Time.y;
                    float v = i.uv.y;
                    float sd = frac(i.seed + i.color.a * 7.31);
                    float ripple = (sin(v * 31.0 + t * 2.3 + sd * 6.28) * 0.6 + sin(v * 73.0 - t * 3.7) * 0.4) * _Ripple * (0.3 + v);
                    float x = (i.uv.x - 0.5) * 2.0 + ripple;
                    float width = lerp(0.18, 0.75, v);
                    float core = exp(-x * x / (width * width));
                    float dashes = saturate(0.55 + 0.45 * sin(v * 46.0 - t * 4.0 + sd * 17.0) + 0.3 * sin(v * 117.0 + t * 1.3));
                    float flicker = 0.85 + 0.15 * sin(t * (7.0 + sd * 5.0) + sd * 40.0);
                    float along = smoothstep(0.0, 0.08, v) * pow(saturate(1.0 - v), 1.6);
                    c.rgb = _Color.rgb * i.color.rgb * (core * dashes * flicker * along);
                    c.a = 1;
                    half3 fw = MixFogColor(c.rgb, half3(0, 0, 0), i.fogFactor);
                    c.rgb = lerp(c.rgb, fw, _FogAmount);
                    return c;
                }
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                if (_EmMask > 0.5)
                {
                    c.rgb = (t.r * _TintR.rgb + t.g * _TintG.rgb + t.b * _TintB.rgb) * t.a;
                    c.a = t.a;
                }
                else
                {
                    c = t * _Color * i.color;
                }
                if (_EdgePow > 0)
                {
                    half ndv = abs(dot(normalize(i.normalWS), normalize(i.viewWS)));
                    half e = pow(saturate(ndv), _EdgePow);
                    c.rgb *= e; c.a *= e;
                }
                half3 fogTarget = (_DstBlend == 1) ? half3(0, 0, 0) : unity_FogColor.rgb; // additive fades out, blended fades to fog
                half3 fogged = MixFogColor(c.rgb, fogTarget, i.fogFactor);
                c.rgb = lerp(c.rgb, fogged, _FogAmount);
                return c;
            }
            ENDHLSL
        }
    }
}
