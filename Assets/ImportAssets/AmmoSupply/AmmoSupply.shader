Shader "Custom/AmmoSupply" {
Properties { [HDR] _Tint("Tint", Color) = (1.5,3,.25,1) _Fade("Fade", Range(0,1)) = 1 }
SubShader { Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
Pass { Tags { "LightMode"="SRPDefaultUnlit" } Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
CBUFFER_START(UnityPerMaterial)
float4 _Tint; float _Fade;
CBUFFER_END
V vert(A a) { V o; o.positionCS=TransformObjectToHClip(a.positionOS.xyz); o.uv=a.uv; return o; }
float4 frag(V i):SV_Target {
float2 p=i.uv*2-1; float r=length(p); float aa=max(fwidth(r),.001);
float a=atan2(p.y,p.x)/6.2831853;
float segment=smoothstep(.10,.16,frac(a*6+_Time.y*.12));
float outer=(1-smoothstep(.018,.018+aa,abs(r-.94)))*segment;
float inner=(1-smoothstep(.008,.008+aa,abs(r-.80)))*step(.45,frac(a*24-_Time.y*.2));
float fill=(1-smoothstep(.85,.94,r))*.045;
return float4(_Tint.rgb, max(fill,max(outer,inner*.5))*_Fade*_Tint.a);
}
ENDHLSL
} } }
