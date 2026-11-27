Shader "Custom/AmmoIcon" {
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
float2 p=i.uv; float mask=0;
[unroll] for(int j=0;j<3;j++) {
float x=p.x-(.26+j*.24); float y=p.y;
float d=length(float2(x,max(y-.67,0)))-.085;
float body=(1-smoothstep(0,.012,d))*smoothstep(.19,.21,y);
float grooves=1-.75*max(1-smoothstep(.008,.016,abs(y-.29)),1-smoothstep(.008,.016,abs(y-.35)));
mask=max(mask,body*grooves);
}
return float4(_Tint.rgb,mask*_Fade*_Tint.a);
}
ENDHLSL
} } }
