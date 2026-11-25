Shader "Skybox/6 Sided Rotated"
{
    Properties
    {
        _Tint ("Tint Color", Color) = (.5, .5, .5, .5)
        [Gamma] _Exposure ("Exposure", Range(0, 8)) = 1.0
        _RotationX ("Rotation X (Up-Down)", Range(0, 360)) = 0
        _RotationY ("Rotation Y (Left-Right)", Range(0, 360)) = 0
        [NoScaleOffset] _FrontTex ("Front [+Z] (HDR)", 2D) = "grey" {}
        [NoScaleOffset] _BackTex ("Back [-Z] (HDR)", 2D) = "grey" {}
        [NoScaleOffset] _LeftTex ("Left [+X] (HDR)", 2D) = "grey" {}
        [NoScaleOffset] _RightTex ("Right [-X] (HDR)", 2D) = "grey" {}
        [NoScaleOffset] _UpTex ("Up [+Y] (HDR)", 2D) = "grey" {}
        [NoScaleOffset] _DownTex ("Down [-Y] (HDR)", 2D) = "grey" {}
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        CGINCLUDE
        #include "UnityCG.cginc"

        half4 _Tint;
        half _Exposure;
        float _RotationX;
        float _RotationY;

        float3 RotateVector(float3 v)
        {
            float rx = _RotationX * UNITY_PI / 180.0;
            float ry = _RotationY * UNITY_PI / 180.0;

            // Y rotation
            float c = cos(ry), s = sin(ry);
            float3 t = float3(v.x * c - v.z * s, v.y, v.x * s + v.z * c);

            // X rotation
            c = cos(rx); s = sin(rx);
            return float3(t.x, t.y * c - t.z * s, t.y * s + t.z * c);
        }

        struct appdata_t {
            float4 vertex : POSITION;
            float2 texcoord : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct v2f {
            float4 vertex : SV_POSITION;
            float2 texcoord : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        v2f vert (appdata_t v)
        {
            v2f o;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            float3 rotated = RotateVector(v.vertex.xyz);
            o.vertex = UnityObjectToClipPos(float4(rotated, 1));
            o.texcoord = v.texcoord;
            return o;
        }

        half4 skybox_frag (v2f i, sampler2D smp, half4 smpDecode)
        {
            half4 tex = tex2D(smp, i.texcoord);
            half3 c = DecodeHDR(tex, smpDecode);
            c = c * _Tint.rgb * unity_ColorSpaceDouble.rgb;
            c *= _Exposure;
            return half4(c, 1);
        }
        ENDCG

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            sampler2D _FrontTex;
            half4 _FrontTex_HDR;
            half4 frag (v2f i) : SV_Target { return skybox_frag(i, _FrontTex, _FrontTex_HDR); }
            ENDCG
        }
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            sampler2D _BackTex;
            half4 _BackTex_HDR;
            half4 frag (v2f i) : SV_Target { return skybox_frag(i, _BackTex, _BackTex_HDR); }
            ENDCG
        }
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            sampler2D _LeftTex;
            half4 _LeftTex_HDR;
            half4 frag (v2f i) : SV_Target { return skybox_frag(i, _LeftTex, _LeftTex_HDR); }
            ENDCG
        }
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            sampler2D _RightTex;
            half4 _RightTex_HDR;
            half4 frag (v2f i) : SV_Target { return skybox_frag(i, _RightTex, _RightTex_HDR); }
            ENDCG
        }
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            sampler2D _UpTex;
            half4 _UpTex_HDR;
            half4 frag (v2f i) : SV_Target { return skybox_frag(i, _UpTex, _UpTex_HDR); }
            ENDCG
        }
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            sampler2D _DownTex;
            half4 _DownTex_HDR;
            half4 frag (v2f i) : SV_Target { return skybox_frag(i, _DownTex, _DownTex_HDR); }
            ENDCG
        }
    }
    Fallback Off
}