Shader "Hidden/TerrainFormer/Terrain Brush Preview" {
	Properties{
		_Color("Main Color", Color) = (0.2, 0.7, 1.0, 0.7)
		_MainTex("Base (RGB) Trans (A)", 2D) = "white" {}
		_OutlineTex("Outline", 2D) = "" {}
	}

	Subshader {
		Tags { 
			"Queue"="Transparent" 
			"IgnoreProjector"="True" 
			"RenderType"="Transparent" 
		}
		Pass {
			ZWrite Off
			Blend SrcAlpha OneMinusSrcAlpha
			Offset -1, -1

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			struct v2f {
				float4 uvMain : TEXCOORD0;
				float4 pos : SV_POSITION;
			};

			float4x4 unity_Projector;

			v2f vert(float4 vertex : POSITION) {
				v2f o;
				o.pos = UnityObjectToClipPos(vertex);
				o.uvMain = mul(unity_Projector, vertex);
				return o;
			}

			fixed4 _Color;
			sampler2D _MainTex;
			sampler2D _OutlineTex;

			fixed4 frag(v2f i) : SV_Target {
				fixed4 mainColor = tex2Dproj(_MainTex, UNITY_PROJ_COORD(i.uvMain));
				mainColor.a += tex2Dproj(_OutlineTex, UNITY_PROJ_COORD(i.uvMain)).a;
				mainColor.a *= _Color.a;
				mainColor.rgb = _Color.rgb;

				return mainColor;
			}
			ENDCG
		}
	}
}