Shader "My Pipeline/Unlit" {

	Properties{
		_Color("Color", Color) = (1, 1, 1, 1)
	}

		SubShader{

			Pass {
				HLSLPROGRAM

				#pragma target 3.5

				#pragma multi_compile_instancing //produces 2 shader variants, one with and one without the INSTANCING_ON keyword defined - instancing isn't always needed
				#pragma instancing_options assumeuniformscaling //disable shader option to support non-uniform scaling

				#pragma vertex UnlitPassVertex
				#pragma fragment UnlitPassFragment

				#include "Unlit.hlsl"

				ENDHLSL
			}
	}
}