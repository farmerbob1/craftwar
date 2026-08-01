Shader "Craftwar/UnitTeamColor"
{
    // Recolors a unit's baked-neutral sprite to one of 8 team colours at draw
    // time, so only one master sprite + one mask need to be baked per frame
    // instead of 8 pre-tinted copies. _MaskTex shares _MainTex's UVs exactly
    // (same atlas layout, baked together) and its R channel holds 0 for "not
    // a team-colour pixel" or shade+1 (1..4) for the WC2 4-shade ramp — see
    // Craftwar.EditorTools.SpriteBaker. _PlayerColor (0-7) is expected to be
    // set per-renderer via a MaterialPropertyBlock.
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _MaskTex ("Team Mask", 2D) = "black" {}
        _PlayerColor ("Player Color Index", Float) = 0
        _Color ("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ PIXELSNAP_ON
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex  : SV_POSITION;
                fixed4 color   : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                #ifdef PIXELSNAP_ON
                OUT.vertex = UnityPixelSnap(OUT.vertex);
                #endif
                return OUT;
            }

            sampler2D _MainTex;
            sampler2D _MaskTex;
            float _PlayerColor;

            // TeamRamps, 8 players x 4 shades (red, blue, green, violet,
            // orange, black, white, yellow), normalized from
            // Craftwar.Import.War2.War2Sprites.TeamRamps.
            static const float3 _TeamRamps[32] = {
                float3(0.2667, 0.0157, 0.0000), float3(0.3608, 0.0157, 0.0000), float3(0.4863, 0.0000, 0.0000), float3(0.6431, 0.0000, 0.0000),
                float3(0.0000, 0.0157, 0.2980), float3(0.0000, 0.0784, 0.4235), float3(0.0000, 0.1412, 0.5804), float3(0.0000, 0.2353, 0.7529),
                float3(0.0000, 0.1569, 0.0471), float3(0.0157, 0.3294, 0.1725), float3(0.0784, 0.5176, 0.3608), float3(0.1725, 0.7059, 0.5804),
                float3(0.1725, 0.0314, 0.1725), float3(0.3137, 0.0627, 0.2980), float3(0.4549, 0.1882, 0.5176), float3(0.5961, 0.2824, 0.6902),
                float3(0.4314, 0.1255, 0.0471), float3(0.5961, 0.2196, 0.0627), float3(0.7686, 0.3451, 0.0627), float3(0.9412, 0.5176, 0.0784),
                float3(0.0471, 0.0471, 0.0784), float3(0.0784, 0.0784, 0.1255), float3(0.1098, 0.1098, 0.1725), float3(0.1569, 0.1569, 0.2353),
                float3(0.1412, 0.1569, 0.2980), float3(0.3294, 0.3294, 0.5020), float3(0.5961, 0.5961, 0.7059), float3(0.8784, 0.8784, 0.8784),
                float3(0.7059, 0.4549, 0.0000), float3(0.8000, 0.6275, 0.0627), float3(0.8941, 0.8000, 0.1569), float3(0.9882, 0.9882, 0.2824),
            };

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, IN.texcoord);
                fixed maskVal = tex2D(_MaskTex, IN.texcoord).r;
                if (maskVal > 0.001)
                {
                    int shade = clamp((int)round(maskVal * 255.0) - 1, 0, 3);
                    int player = clamp((int)_PlayerColor, 0, 7);
                    c.rgb = _TeamRamps[player * 4 + shade];
                }
                c *= IN.color;
                c.rgb *= c.a;
                return c;
            }
        ENDCG
        }
    }
}
