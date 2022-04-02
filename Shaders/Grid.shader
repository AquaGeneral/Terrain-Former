Shader "Hidden/TerrainFormer/Grid" {
	Properties {
		_MainTex("Base (RGB) Trans (A)", 2D) = "white" {}
	}

	SubShader {
		Tags {
			"Queue"           = "Transparent" 
			"IgnoreProjector" = "True" 
			"RenderType"      = "Transparent"
		}
	
		ZWrite Off
		Blend SrcAlpha OneMinusSrcAlpha 
		Lighting Off
		Offset -1, -1

		Pass {  
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			struct appdata_t {
				float4 vertex   : POSITION;
				float2 texcoord : TEXCOORD0;
			};

			struct v2f {
				float4 vertex   : SV_POSITION;
				half2 texcoord  : TEXCOORD0;
				float2 localPos : TEXCOORD1;
			};

			sampler2D _MainTex;
			float4 _MainTex_ST;

			v2f vert(appdata_t v) {
				v2f o;
				o.localPos = v.vertex.xy;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
				return o;
			}
			
			fixed4 frag(v2f i) : SV_Target {
				fixed4 colour = tex2D(_MainTex, i.texcoord);

				colour.a *= smoothstep(1.0, 0.3, length(i.localPos.xy) * 2);

				return colour;
			}
			ENDCG
		}
	}
}