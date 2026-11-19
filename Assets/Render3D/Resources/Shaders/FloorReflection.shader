// Proto3D/FloorReflection: planar reflection overlay for the deck, puddles and flood water (FloorReflection3D).
// Samples _ReflTex, a low-res mirrored render of the scene above the floor plane, at the fragment's screen position
// (v flipped: the mirror camera is a proper camera, so its image is the reflection upside down).
// Wetness comes from a mask: _MaskFromUv = 0 reads the street metal/gloss map's smoothness (A) at object XZ / _MaskUnits
// (the deck's own UV mapping, so reflections sit in the painted puddles); 1 reads the puddle mask's alpha at UV0;
// 2 is fully wet (flood water). Damp areas get a short vertical smear (Replaced's streaky wet floor), puddles a
// near-clean mirror with a slow world-anchored ripple. Grazing view raises reflectance (Schlick-style).
// Premultiplied blend over the opaque floor: rgb = refl * a, alpha = a * _Cover. 1 mask + 5 reflection taps.
// Target: URP 17 Forward+ (Unity 6.3), desktop Linux/Windows; no keywords, one variant. _MaskFromUv is a
// material-uniform branch (coherent per draw), not per-pixel divergence.
Shader "Proto3D/FloorReflection"
{
    Properties
    {
        _ReflTex ("Reflection (set by FloorReflection3D)", 2D) = "black" {}
        _MaskTex ("Wet mask", 2D) = "white" {}
        _MaskUnits ("Mask tile size (world units)", Float) = 8
        _MaskFromUv ("Mask mode (0 gloss XZ, 1 UV alpha, 2 wet)", Float) = 0
        _MaskLo ("Wet threshold low", Float) = 0.6
        _MaskHi ("Wet threshold high", Float) = 0.93
        _Damp ("Reflectance on damp floor", Float) = 0.16
        _Puddle ("Reflectance in puddles", Float) = 0.85
        [HDR] _Tint ("Tint", Color) = (1,1,1,1)
        _Cover ("Base cover (darkens under the reflection)", Float) = 0.6
        _Smear ("Vertical smear on damp floor (uv)", Float) = 0.035
        _Ripple ("Ripple (uv)", Float) = 0.004
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-40" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "FloorReflection"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_ReflTex); SAMPLER(sampler_ReflTex);
            TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _ReflTex_ST;
                float4 _MaskTex_ST;
                float _MaskUnits;
                float _MaskFromUv;
                half _MaskLo, _MaskHi, _Damp, _Puddle, _Cover, _Smear, _Ripple;
                half4 _Tint;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 screenPos : TEXCOORD0; float2 maskUv : TEXCOORD1; float3 posWS : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.posWS);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.maskUv = _MaskFromUv > 0.5 ? v.uv : v.positionOS.xz / _MaskUnits;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half wet;
                if (_MaskFromUv > 1.5) wet = 1;
                else if (_MaskFromUv > 0.5) wet = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.maskUv).a;
                // Gloss mode reads mip 3 (8×8 texels averaged): puddles survive, wet joints between setts and
                // planks average out, so the mirror never shows as dotted single-texel glints.
                else wet = smoothstep(_MaskLo, _MaskHi, SAMPLE_TEXTURE2D_LOD(_MaskTex, sampler_MaskTex, i.maskUv, 3).a);

                float2 suv = i.screenPos.xy / i.screenPos.w;
                float t = _Time.y;
                // World-anchored ripple bands ~0.5 units deep (≥ 2 texels), slow: no single-texel shimmer.
                float rip = sin(i.posWS.z * 6.0 + t * 1.1) * sin(i.posWS.x * 0.9 + i.posWS.z * 1.7 - t * 0.5);
                float2 ruv = float2(suv.x + rip * _Ripple, 1.0 - suv.y);
                float s = _Smear * (1.0 - wet * 0.85);
                half3 r = SAMPLE_TEXTURE2D(_ReflTex, sampler_ReflTex, ruv).rgb * 0.36
                        + SAMPLE_TEXTURE2D(_ReflTex, sampler_ReflTex, ruv + float2(0, s)).rgb * 0.22
                        + SAMPLE_TEXTURE2D(_ReflTex, sampler_ReflTex, ruv - float2(0, s)).rgb * 0.22
                        + SAMPLE_TEXTURE2D(_ReflTex, sampler_ReflTex, ruv + float2(0, s * 2.2)).rgb * 0.1
                        + SAMPLE_TEXTURE2D(_ReflTex, sampler_ReflTex, ruv - float2(0, s * 2.2)).rgb * 0.1;

                float3 V = normalize(GetWorldSpaceViewDir(i.posWS));
                half fres = 0.35 + 0.65 * pow(1.0 - saturate(V.y), 3.0);
                half a = saturate(lerp(_Damp, _Puddle, wet) * fres);
                return half4(r * _Tint.rgb * a, a * _Cover);
            }
            ENDHLSL
        }
    }
}
