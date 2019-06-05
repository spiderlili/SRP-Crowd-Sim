#ifndef MYRP_UNLIT_INCLUDED 
#define MYRP_UNLIT_INCLUDED 
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl" //include core library for macros
#pragma target 3.5
//produces 2 shader variants, one with and one without the INSTANCING_ON keyword defined - instancing isn't always needed
#pragma multi_compile_instancing
#pragma vertex UnlitPassVertex
#pragma fragment UnlitPassFragment
#pragma instancing_options assumeuniformscaling //disable shader option to support non-uniform scaling

/* cbuffer don't benefit all platforms - use macros instead
cbuffer UnityPerDraw{
	float4x4 unity_ObjectToWorld; //transformation model matrix to convert from object space to world space
};
cbuffer UnityPerFrame {
	float4x4 unity_MatrixVP;
};*/

CBUFFER_START(UnityPerFrame)
float4x4 unity_MatrixVP;
CBUFFER_END

CBUFFER_START(UnityPerDraw)
float4x4 unity_ObjectToWorld;
CBUFFER_END

CBUFFER_START(UnityPerMaterial)
float4 _Color;
CBUFFER_END

//use unity_ObjectToWorld when not instancing, or a matrix array when instancing
#define UNITY_MATRIX_M unity_ObjectToWorld
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

UNITY_INSTANCING_BUFFER_START(PerInstance)
UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
UNITY_INSTANCING_BUFFER_END(PerInstance)

struct VertexInput {
	float4 pos : POSITION; 
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertexOutput {
	float4 clipPos : SV_POSITION; //clipspace vert pos
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

VertexOutput UnlitPassVertex(VertexInput input) {
	VertexOutput output;
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_TRANSFER_INSTANCE_ID(input, output);
	//optimse for compiler: a full matrix multiplication w a 4d position vector is not needed - the 4th of pos is always 1!
	//float4 worldPos = mul(unity_ObjectToWorld, float4(input.pos.xyz, 1.0)); 
	float4 worldPos = mul(UNITY_MATRIX_M, float4(input.pos.xyz, 1.0)); //shader support for GPU instancing: put an array containng the M matrices of all obj in a const buffer
	output.clipPos = mul(unity_MatrixVP, worldPos); //convert from world space to clip space w a view-projection matrix
	return output;
}

//receives the interpolated vertex output as input
float4 UnlitPassFragment(VertexOutput input) : SV_TARGET{
	UNITY_SETUP_INSTANCE_ID(input);
	return UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Color);
	//return _Color;
	//return 1; //Default white colour for frag
}


#endif // MYRP_UNLIT_INCLUDED