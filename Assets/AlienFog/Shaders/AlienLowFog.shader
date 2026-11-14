// Alien-style low-lying fog sprite shader.
// Unlit, transparent, works in the Built-in pipeline AND URP (no LightMode tag -> rendered as an unlit pass).
// Features: height fade (fog hugs the floor and thins out toward its top), colour gradient by height,
// cheap fake light glow from up to 4 lights, fade near the camera, optional soft intersection with geometry.
Shader "AlienFog/LowFog"
{
    Properties
    {
        _MainTex ("Smoke sheet (RGBA)", 2D) = "white" {}
        _ColorLow ("Colour near floor", Color) = (0.50, 0.60, 0.64, 1)
        _ColorHigh ("Colour near top", Color) = (0.82, 0.92, 0.95, 1)
        _Opacity ("Opacity", Range(0, 3)) = 1
        _FogFloor ("Floor Y", Float) = 0
        _FogTop ("Top Y", Float) = 1.5
        _FadeBottom ("Fade in above floor (m)", Float) = 0.12
        _FadeTop ("Fade out below top (m)", Float) = 0.6
        _NearFade ("Fade near camera (m)", Float) = 1.5
        _Soft ("Soft particles 0/1 (needs depth texture)", Range(0, 1)) = 0
        _SoftDistance ("Soft distance (m)", Float) = 1.0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _ColorLow;
            fixed4 _ColorHigh;
            float _Opacity;
            float _FogFloor;
            float _FogTop;
            float _FadeBottom;
            float _FadeTop;
            float _NearFade;
            float _Soft;
            float _SoftDistance;
            // fake light glow (filled by the AlienLowFog component): xyz = position, w = range / rgb = colour * strength
            float4 _FogLightPos[4];
            float4 _FogLightCol[4];
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 wpos : TEXCOORD1;
                float4 proj : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.proj = ComputeScreenPos(o.pos);
                COMPUTE_EYEDEPTH(o.proj.z);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv);

                // height gradient + height fade (this is what stops it looking like a flat plane)
                float h = i.wpos.y - _FogFloor;
                float span = max(_FogTop - _FogFloor, 0.001);
                float t = saturate(h / span);
                float hFade = saturate(h / max(_FadeBottom, 0.001)) * saturate((_FogTop - i.wpos.y) / max(_FadeTop, 0.001));

                fixed3 col = lerp(_ColorLow.rgb, _ColorHigh.rgb, t) * tex.rgb * i.color.rgb;

                // cheap light glow: fog near a light picks up its colour (like the lasers / backlight in the egg chamber)
                float3 glow = float3(0, 0, 0);
                for (int k = 0; k < 4; k++)
                {
                    float3 d = _FogLightPos[k].xyz - i.wpos;
                    float r = max(_FogLightPos[k].w, 0.001);
                    float att = saturate(1.0 - dot(d, d) / (r * r));
                    glow += _FogLightCol[k].rgb * (att * att);
                }
                col += glow * tex.a;

                float a = tex.a * i.color.a * _Opacity * hFade;

                // fade out when very close to the camera (no sudden white-out when walking into fog)
                float eye = i.proj.z;
                a *= saturate((eye - _NearFade * 0.25) / max(_NearFade, 0.001));

                // optional soft intersection with walls/props
                float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.proj)));
                float soft = saturate((scene - eye) / max(_SoftDistance, 0.001));
                a *= lerp(1.0, soft, _Soft);

                return fixed4(col, saturate(a));
            }
            ENDCG
        }
    }
    FallBack Off
}
