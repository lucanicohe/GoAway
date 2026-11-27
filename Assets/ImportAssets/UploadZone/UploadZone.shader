Shader "Custom/UploadZone" {
Properties { [HDR] _Tint("Tint", Color) = (0,2,3,1) _Fill("Fill", Range(0,0.3)) = 0.05 _Pulse("Pulse", Float) = 1 }
SubShader {
Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
Pass {
Tags { "LightMode"="SRPDefaultUnlit" }
Blend SrcAlpha OneMinusSrcAlpha
ZWrite Off
Cull Off
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
CBUFFER_START(UnityPerMaterial)
float4 _Tint; float _Fill; float _Pulse;
CBUFFER_END
V vert(A a) { V o; o.positionCS=TransformObjectToHClip(a.positionOS.xyz); o.uv=a.uv; return o; }
float4 frag(V i):SV_Target {
float2 p=(i.uv-.5)*2; float r=length(p); float aa=max(fwidth(r),.001);
float inside=1-smoothstep(1-aa,1,r);
float ring=1-smoothstep(.009,.009+aa,abs(r-.98));
float angle=atan2(p.y,p.x)/6.2831853;
float dash=smoothstep(.25,.35,frac(angle*32-_Time.y*.35));
float inner=(1-smoothstep(.006,.006+aa,abs(r-.93)))*dash;
float pulse=.85+.15*sin(_Time.y*3*_Pulse);
float alpha=max(_Fill*inside,max(ring*.85,inner*.45))*inside;
return float4(_Tint.rgb*pulse,alpha*_Tint.a);
}
ENDHLSL
}
}
}
