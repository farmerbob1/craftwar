// Fog of war overlay: one quad covering the map, sampling a per-tile mask.
//
// The mask texture is one texel per tile, bilinear, so the hardware
// interpolates between tiles and the fog edge comes out soft for free rather
// than as a 32px staircase. R = visible now, G = explored ever.
//
// Unlit and alpha-blended: this draws over the tilemap and units, so it must
// not participate in 2D lighting or write depth.
Shader "Craftwar/FogOfWar"
{
    Properties
    {
        _MaskTex ("Visibility Mask (R=visible, G=explored)", 2D) = "black" {}
        _FogColor ("Fog Color", Color) = (0, 0, 0, 1)
        _ExploredAlpha ("Explored Dim", Range(0, 1)) = 0.5
        _UnexploredAlpha ("Unexplored Dim", Range(0, 1)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Lighting Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MaskTex_ST;
                float4 _FogColor;
                float _ExploredAlpha;
                float _UnexploredAlpha;
            CBUFFER_END

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

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MaskTex);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half2 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, IN.uv).rg;
                half visible = mask.r;
                half explored = mask.g;

                // Unexplored is fully dark; explored-but-not-visible is dimmed;
                // currently visible is clear. Both lerps run on the interpolated
                // mask, so the transitions are smooth.
                half alpha = lerp(_UnexploredAlpha, _ExploredAlpha, explored);
                alpha = lerp(alpha, 0.0h, visible);

                return half4(_FogColor.rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
