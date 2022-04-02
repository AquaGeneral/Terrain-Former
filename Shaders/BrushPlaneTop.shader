Shader "Hidden/TerrainFormer/BrushPlaneTop" {
	Properties {
		_Color("Main Color", Color) = (0.2, 0.7, 1.0, 0.7)
		_MainTex("Main Texture", 2D) = "white" {}
		_OutlineTex("Outline", 2D) = "" {}
	}

	SubShader {
		Tags {
			"Queue" = "Transparent"
			"IgnoreProjector" = "True"
			"RenderType" = "Transparent"
		}
		Lighting Off ZWrite Off
		Blend SrcAlpha OneMinusSrcAlpha
		Offset -1, -1

		CGINCLUDE
		#include "UnityCG.cginc"
		struct appdata_t {
			float4 vertex   : POSITION;
			float2 texcoord : TEXCOORD0;
		};

		struct v2f {
			float4 vertex    : SV_POSITION;
			float2 texcoord  : TEXCOORD0;
		};

		fixed4 _Color;
		sampler2D _MainTex;
		sampler2D _OutlineTex;
		sampler2D _CameraDepthTexture;

		inline fixed4 GetColour(v2f i) {
			fixed4 colour;
			colour.rgb = _Color.rgb;
			colour.a = tex2D(_MainTex, i.texcoord).a + tex2D(_OutlineTex, i.texcoord).a;
			colour.a *= _Color.a;
			return colour;
		}

		v2f vert(appdata_t v) {
			v2f o;
			o.vertex = UnityObjectToClipPos(v.vertex);
			o.texcoord = v.texcoord;
			return o;
		}
		ENDCG

		Pass {
			ZTest LEqual

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			fixed4 frag (v2f i) : SV_Target {
				return GetColour(i);
			}
			ENDCG 
		}

		Pass {
			ZTest Greater

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			fixed4 frag (v2f i) : SV_Target {
				fixed4 c = GetColour(i);
				c.rgb *= 1.2;
				c.a *= 0.4;
				return c;
			}
			ENDCG 
		}
	}
}