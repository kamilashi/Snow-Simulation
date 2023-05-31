Shader "Custom/GridShader"
{

	Properties{
		_Color("Color", Color) = (0,0,0,1)
		[Toggle(SHOW_FORCE)] _Show_Force("Show Force", Float) = 0
		[Toggle(SHOW_CNOWPARAMS)] _Show_SnowParams("Show Snow Paramt", Float) = 0
		//_HeightMap("Albedo (RGB)", 2D) = "white" {}
	}

	SubShader{
		Tags{ "RenderType" = "Transparent" "Queue" = "Transparent" }

		LOD 200
		Cull Off
		ZWrite Off
		Blend SrcAlpha OneMinusSrcAlpha

		CGPROGRAM

		//sampler2D _HeightMap;

		struct Input {
				float2 uv;
				};

		fixed4 _Color;
		float3 _Position;
		float _Show_Force;
		float _Show_SnowParams;
		float _CellSize;
		float _Metallic;

		#pragma surface surf Standard vertex:vert addshadow fullforwardshadows alpha:fade
		#pragma instancing_options procedural:setup

		float4x4 _Matrix;

		float4x4 scale_matrix(float scale) {
			return float4x4(
				scale, 0, 0, 0,
				0, scale, 0, 0,
				0, 0, scale, 0,
				0, 0, 0, 1
				);
		}
		 #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
		struct Cell
		{
			int3 gridIndex;
			float3 WSposition;
			float3 force;
			float density;
			float hardness;
			float temperature;
			float grainSize;
			float mass;
			int index;
			int isOccupied; //TO-DO - enum here
		};

		StructuredBuffer<Cell> cellGridBuffer;
		 #endif


	 void vert(inout appdata_full v, out Input data)
	{
		UNITY_INITIALIZE_OUTPUT(Input, data);

		#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
			//v.vertex = mul(_Matrix, v.vertex);
			v.vertex.xyz *= _CellSize;
			v.vertex.xyz += _Position;
		#endif
	}

	void setup()
	{
		#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
			Cell cell = cellGridBuffer[unity_InstanceID];
			_Position = cell.WSposition;
			int content = cell.isOccupied;
			if(_Show_SnowParams){

				if (content == -2) {
					_Color.r = 0.0f;
					_Color.g = 0.0f; //content is the index of the particle that the cell is occupied by
					_Color.b = 0.0f;
					_Color.a = 0.0f;
				}
				if (content == -1) { //only air
					_Color.r = 0.0f;
					_Color.g = 0.0f; //content is the index of the particle that the cell is occupied by
					_Color.b = 0.0f;
					_Color.a = 0.0f;
				}
				if (content > -1) {
					_Color.r = 0.0f; //cell.mass;
					_Color.g = 0.0f; //content is the index of the particle that the cell is occupied by
					_Color.b = cell.density;
					_Color.a = 1.0f;
				}
			}

			if ((_Show_Force)) {
				float3 force = (cell.force );
				_Color.r = force.x;
				_Color.g = force.y;
				_Color.b = force.z;

				_Color.a = 1.0f * saturate(length(force));
			}
			
		#endif
	}

	 void surf(Input IN, inout SurfaceOutputStandard o) {
		//fixed4 c = _Color;
		o.Metallic = 0;
		o.Smoothness = 1;
		//float heightSample = tex2D(_HeightMap, IN.uv);
		o.Albedo = _Color.rgb;
		o.Alpha = _Color.a;
	  }

	  ENDCG
	}
		//FallBack "Diffuse"
}
