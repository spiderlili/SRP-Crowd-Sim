#ifndef MYRP_UNLIT_INCLUDED 
#define MYRP_UNLIT_INCLUDED 

struct VertexInput {
	float4 pos : POSITION;
};

struct VertexOutput {
	float4 clipPos : SV_POSITION;
};

#endif // MYRP_UNLIT_INCLUDED