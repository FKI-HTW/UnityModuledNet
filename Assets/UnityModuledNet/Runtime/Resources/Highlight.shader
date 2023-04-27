Shader "UnitySync/Highlight" {
	Properties{
		_Color("Color", Color) = (0,0,0,1)
		_Width("Outline Width", Range(0, 1)) = .1
	}

	CGINCLUDE
	#include "UnityCG.cginc"

	struct appdata {
		float4 vertex : POSITION;
		float3 normal : NORMAL;
	};

	struct v2f {
		float4 pos : POSITION;
		float4 color : COLOR;
	};

	uniform float _Width;
	uniform float4 _Color;

	v2f vert(appdata v) {
		v2f o;
		o.pos = UnityObjectToClipPos(v.vertex);

		float3 norm = mul((float3x3)UNITY_MATRIX_IT_MV, v.normal);
		float2 offset = TransformViewToProjection(norm.xy);

		o.pos.xy += offset * o.pos.z * _Width;
		o.color = _Color;
		return o;
	}
	ENDCG

	SubShader{
		Tags { "Queue" = "Transparent" "IgnoreProjector" = "True"}
		Cull Back
		

		Pass {
			Name "OUTLINE"
			Tags { "Queue" = "Transparent" "IgnoreProjector" = "True"}
			Cull Front
			ZWrite Off

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			half4 frag(v2f i) :COLOR { return i.color; }
			ENDCG
		}
	}

	Fallback "Diffuse"
}