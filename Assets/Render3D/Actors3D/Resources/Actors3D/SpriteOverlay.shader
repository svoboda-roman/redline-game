// Actors3D/SpriteOverlay: the unlit layer drawn just in front of a lit sprite-sheet character in the 2.5D scene.
// One draw per character carries everything the 2D renderer spread over five SpriteRenderers:
//   emissive  = maskR * _TintR + maskG * _TintG + maskB * _TintB      (HDR: attack glow, eyes, strips; blooms)
//   fill      = colour * _Fill.rgb * _Fill.a                          (readability floor away from lamps)
//   rim       = _Rim.rgb * exterior * facing                           (thin cool backlight on the OUTER silhouette)
//     exterior: an opaque texel with at least one transparent 4-neighbour (1 texel at native sprite resolution).
//               Neighbours outside the frame rect (_FrameST) count as transparent, so atlas neighbours never leak.
//     facing:   lerp(0, 1, outward edge normal . up) with sides at _Rim.a: the scene's character rims come from
//               behind and above, so top edges catch most, side edges some, bottom edges none.
//     The sprite normal map is NOT used for the rim: its bevels made every interior arm/leg/hat edge glow.
//   flash     = _Flash.rgb, coverage _Flash.a                          (hit flash, telegraph blinks)
// Premultiplied blend (One, OneMinusSrcAlpha): emissive/fill/rim add, the flash covers the lit body.
// Frame selection: _FrameST (xy scale, zw offset into the atlas), set per renderer by MaterialPropertyBlock.
// _Alpha fades everything (corpses). Masks are the white+alpha atlases CharacterSheet splits from _emissive.
// The normal sheet (_BumpTex) is kept bound for the lit body material's sake but no longer sampled here.
// Target: URP Forward/Forward+, all tiers. Cost: 9 point samples (colour, 3 masks, 4 alpha taps), no branches
// besides the transparent-texel discard, no keywords/variants.
Shader "Actors3D/SpriteOverlay"
{
    Properties
    {
        _ColorTex ("Colour Atlas", 2D) = "white" {}
        _BumpTex ("Normal Atlas (linear)", 2D) = "bump" {}
        _MaskR ("Mask R (alpha)", 2D) = "black" {}
        _MaskG ("Mask G (alpha)", 2D) = "black" {}
        _MaskB ("Mask B (alpha)", 2D) = "black" {}
        _FrameST ("Frame ST", Vector) = (1,1,0,0)
        [HDR] _TintR ("Tint R", Color) = (0,0,0,0)
        [HDR] _TintG ("Tint G", Color) = (0,0,0,0)
        [HDR] _TintB ("Tint B", Color) = (0,0,0,0)
        _Fill ("Fill (rgb tint, a amount)", Color) = (1,1,1,0)
        [HDR] _Flash ("Flash (rgb, a coverage)", Color) = (1,1,1,0)
        [HDR] _Rim ("Rim (rgb colour, a side-edge weight 0..1)", Color) = (0,0,0,0.5)
        _Alpha ("Alpha", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Overlay"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_ColorTex); SAMPLER(sampler_ColorTex);
            TEXTURE2D(_BumpTex);
            TEXTURE2D(_MaskR); TEXTURE2D(_MaskG); TEXTURE2D(_MaskB);
            CBUFFER_START(UnityPerMaterial)
                float4 _ColorTex_ST;
                float4 _ColorTex_TexelSize;
                float4 _FrameST;
                half4 _TintR; half4 _TintG; half4 _TintB;
                half4 _Fill; half4 _Flash; half4 _Rim;
                half _Alpha;
            CBUFFER_END

            // Alpha above this counts as body (matches the point-sampled pixel-art cut-out).
            #define BODY_ALPHA_CUT 0.5h
            // Top edges get the full rim; side edges get _Rim.a; bottom edges get none (backlight from above).
            #define RIM_TOP_WEIGHT 1.0h

            // Alpha of the texel at uv + offset, 0 when that texel is outside the current frame rect.
            half FrameAlpha(float2 uv, float2 offset, float2 rectMin, float2 rectMax)
            {
                float2 p = uv + offset;
                half inside = (half)(step(rectMin.x, p.x) * step(rectMin.y, p.y) * step(p.x, rectMax.x) * step(p.y, rectMax.y));
                return step(BODY_ALPHA_CUT, SAMPLE_TEXTURE2D(_ColorTex, sampler_ColorTex, p).a) * inside;
            }

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv * _FrameST.xy + _FrameST.zw;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Point sampling everywhere (the sheets are pixel art; atlases are imported point-filtered).
                half4 col = SAMPLE_TEXTURE2D(_ColorTex, sampler_ColorTex, i.uv);
                half mr = SAMPLE_TEXTURE2D(_MaskR, sampler_ColorTex, i.uv).a;
                half mg = SAMPLE_TEXTURE2D(_MaskG, sampler_ColorTex, i.uv).a;
                half mb = SAMPLE_TEXTURE2D(_MaskB, sampler_ColorTex, i.uv).a;
                half3 em = mr * _TintR.rgb + mg * _TintG.rgb + mb * _TintB.rgb;
                half body = step(BODY_ALPHA_CUT, col.a);
                if (body < 0.5h && max(em.r, max(em.g, em.b)) <= 0.0h) discard;

                // Exterior silhouette: 4 neighbour taps 1 texel away, clamped to this frame's rect in the atlas.
                float2 texel = _ColorTex_TexelSize.xy;
                float2 rectMin = _FrameST.zw;
                float2 rectMax = _FrameST.zw + _FrameST.xy;
                half aL = FrameAlpha(i.uv, float2(-texel.x, 0), rectMin, rectMax);
                half aR = FrameAlpha(i.uv, float2( texel.x, 0), rectMin, rectMax);
                half aD = FrameAlpha(i.uv, float2(0, -texel.y), rectMin, rectMax);
                half aU = FrameAlpha(i.uv, float2(0,  texel.y), rectMin, rectMax);
                half exterior = 1.0h - aL * aR * aD * aU;          // 1 when any neighbour is transparent
                // Outward edge normal's vertical part: +1 on a top edge (empty above, body below), -1 on a bottom edge.
                half up = aD - aU;
                // Sides (up = 0) get _Rim.a, top edges RIM_TOP_WEIGHT, bottom edges 0; a thin 1-px strip (both
                // sides empty) reads as a side edge.
                half facing = saturate(lerp(_Rim.a, RIM_TOP_WEIGHT, up) * step(-0.5h, up));
                half3 rim = _Rim.rgb * exterior * facing;
                half3 fill = col.rgb * _Fill.rgb * _Fill.a;
                half cover = saturate(_Flash.a) * body;

                half3 rgb = (fill + rim) * body * (1.0h - cover) + _Flash.rgb * cover + em;
                return half4(rgb * _Alpha, cover * _Alpha);
            }
            ENDHLSL
        }
    }
}
