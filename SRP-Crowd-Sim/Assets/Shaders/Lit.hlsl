#ifndef MYRP_LIT_INCLUDED
#define MYRP_LIT_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl" //include core library for macros

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
float4 unity_LightIndicesOffsetAndCount;
float4 unity_4LightIndices0, unity_4LightIndices1;
CBUFFER_END

#define MAX_VISIBLE_LIGHTS 16

//fill in buffer - pass light data to gpu
CBUFFER_START(_LightBuffer)
float4 _VisibleLightColors[MAX_VISIBLE_LIGHTS];
float4 _VisibleLightDirectionsOrPositions[MAX_VISIBLE_LIGHTS];
float4 _VisibleLightAttenuations[MAX_VISIBLE_LIGHTS];
float4 _VisibleLightSpotDirections[MAX_VISIBLE_LIGHTS];
CBUFFER_END

//uses light data to take care of lighting calculation: extract data from arrays, perform diffuse calc, return mod by light's colour
float3 DiffuseLight(int index, float3 normal) {
	float3 lightColor = _VisibleLightColors[index].rgb;
	float3 lightDirection = _VisibleLightDirections[index].xyz;
	float diffuse = saturate(dot(normal, lightDirection));
	return diffuse * lightColor;
}

/*
CBUFFER_START(UnityPerMaterial)
float4 _Color;
CBUFFER_END*/

//use unity_ObjectToWorld when not instancing, or a matrix array when instancing
#define UNITY_MATRIX_M unity_ObjectToWorld
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

UNITY_INSTANCING_BUFFER_START(PerInstance)
UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
UNITY_INSTANCING_BUFFER_END(PerInstance)

//normal vector for lighting calculation - obj orientation
struct VertexInput {
	float4 pos : POSITION; 
	float3 normal : NORMAL; 
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertexOutput {
	float4 clipPos : SV_POSITION; //clipspace vert pos
	float3 normal : TEXCOORD0;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

VertexOutput LitPassVertex(VertexInput input) {
	VertexOutput output;
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_TRANSFER_INSTANCE_ID(input, output);
	//optimse for compiler: a full matrix multiplication w a 4d position vector is not needed - the 4th of pos is always 1!
	//float4 worldPos = mul(unity_ObjectToWorld, float4(input.pos.xyz, 1.0)); 
	float4 worldPos = mul(UNITY_MATRIX_M, float4(input.pos.xyz, 1.0)); //shader support for GPU instancing: put an array containng the M matrices of all obj in a const buffer
	output.clipPos = mul(unity_MatrixVP, worldPos); //convert from world space to clip space w a view-projection matrix
	output.normal = mul((float3x3)UNITY_MATRIX_M, input.normal); //convert normal from obj to world using uniform scales
	return output;
}

//receives the interpolated vertex output as input
float4 LitPassFragment(VertexOutput input) : SV_TARGET{
	UNITY_SETUP_INSTANCE_ID(input);
	input.normal = normalize(input.normal); //normalisation per fragment 
	float3 albedo = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Color).rgb;

	//use for to invoke the new func once per light => total diffuse light affecting fragment
	float3 diffuseLight = 0;
	for (int i = 0; i < MAX_VISIBLE_LIGHTS; i++) {
		diffuseLight += DiffuseLight(i, input.normal);
	}
	float3 color = diffuseLight * albedo;
	return float4(color, 1);
	//return _Color;
	//return 1; //Default white colour for frag
}


#endif // MYRP_LIT_INCLUDED