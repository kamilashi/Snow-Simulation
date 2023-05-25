Shader "Custom/GridShader"
{

	Properties{
		_Color("Color", Color) = (0,0,0,1)
		[Toggle(SHOW_VELOCITY)] _Show_Velocity("Show Velocity", Float) = 0
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
		float _Show_Velocity;
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
			float3 velocity;
			float3 acceleration;
			int isOccupied; //TO-DO - enum here
			float pressure;
			float density;
			float humidity;
			int index;
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
			if(_Show_SnowParams){

				int content = cell.isOccupied;

				if (content == -2) {
					_Color.rgb = (0, 0, 0); //bedrock
					_Color.a = 0.5f;
				}
				if (content == -1) { //only air
					_Color.r = 0;
					_Color.g = 0; //content is the index of the particle that the cell is occupied by
					_Color.b = cell.humidity;
					_Color.a = 0.5f;
				}
				if (content >= 0) {
					_Color.r = cell.pressure;
					_Color.g = 1; //content is the index of the particle that the cell is occupied by
					_Color.b = cell.humidity;
					_Color.a = 0.5f;
				}
			}

			if (_Show_Velocity) {
				_Color.r = (cell.velocity.x);
				_Color.g = (cell.velocity.y);
				_Color.b = (cell.velocity.z);

				//_Color.a = abs((cell.velocity.y + cell.velocity.x + cell.velocity.z)/3.0f);
				_Color.a = length(cell.velocity);
				//_Metallic = abs(cell.velocity);
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
