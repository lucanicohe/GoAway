Shader "Custom/PlanetAtmosphereHalf"
{
    Properties
    {
        _AtmosphereColor ("Bright Atmosphere", Color) = (0.2, 0.5, 1, 1)
        _DarkColor ("Dark Atmosphere", Color) = (0.02, 0.04, 0.15, 1)

        _Power ("Fresnel Power", Range(0.5, 8)) = 2.5
        _Intensity ("Glow Intensity", Range(0, 5)) = 1.5
        _Softness ("Half Softness", Range(0.01, 2)) = 0.4

        [Toggle] _FlipSide ("Flip Side", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            float4 _AtmosphereColor;
            float4 _DarkColor;

            float _Power;
            float _Intensity;
            float _Softness;
            float _FlipSide;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;

                o.pos = UnityObjectToClipPos(v.vertex);

                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = normalize(WorldSpaceViewDir(v.vertex));

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 normal = normalize(i.worldNormal);
                float3 viewDir = normalize(i.viewDir);

                // Fresnel atmosphere
                float fresnel = pow(
                    1.0 - saturate(dot(normal, viewDir)),
                    _Power
                );

                // Hemisphere selection
                float side = normal.x;

                if (_FlipSide > 0.5)
                    side = -side;

                float halfMask = smoothstep(
                    -_Softness,
                    _Softness,
                    side
                );

                // Bright and dark atmospheric colors
                float3 atmosphere = lerp(
                    _DarkColor.rgb,
                    _AtmosphereColor.rgb,
                    halfMask
                );

                // Transparent interior, visible atmosphere at edges
                float alpha = saturate(fresnel * _Intensity);

                float3 finalColor = atmosphere * _Intensity;

                return fixed4(finalColor, alpha);
            }

            ENDCG
        }
    }
}