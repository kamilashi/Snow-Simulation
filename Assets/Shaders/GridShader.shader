Shader "Custom/GridShader"
{

	Properties{
		_Color("Color", Color) = (0,0,0,1)
		[Toggle(SHOW_Pressure)] _Show_Pressure("Show Pressure", Float) = 0
		[Toggle(SHOW_SNOWPARAMS)] _Show_Density("Show Density", Float) = 0
		[Toggle(SHOW_SNOWPARAMS)] _Show_Temperature("Show Temperature", Float) = 0
		[Toggle(SHOW_INDEXES)] _Show_Indexes("Show Indexes", Float) = 0
		_Blend_Modifier("Blend Modifier", Float) = 1
	}

	SubShader{
		Tags{ "RenderType" = "Transparent" "Queue" = "Transparent" }

		LOD 200
		Cull Off
		ZWrite Off
		Blend SrcAlpha OneMinusSrcAlpha

		CGPROGRAM

		struct Input {
				float2 uv;
				};

		fixed4 _Color;
		float3 _Position;
		float _Show_Pressure;
		float _Show_Density;
		float _Show_Temperature;
		float _Show_Indexes;
		float _CellSize;
		float _Metallic;

		float _MaxSnowDensity;
		float _MinTemperature;
		float _Blend_Modifier;

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
			float3 pressure;
			float density;
			float indentAmount;
			float hardness;
			float temperature;
			float grainSize;
			float mass;
			float massOver;
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
			//content is the index of the particle that the cell is occupied by
			int content = cell.isOccupied;
			float blend = 1;

			if(_Show_Density){
				blend += _Blend_Modifier;

				if (content == 1) {
					float scale = ((float)cell.density / (float)_MaxSnowDensity) * 1.0f;
					_Color.r = 0.0f;
					_Color.g = 0.0f; 
					_Color.b = lerp(0.0f, 1.0f, scale);
					_Color.a = 1.0f;

				}
				else {
					_Color = float4(0, 0, 0, 0);
				}
			}

			if (_Show_Temperature) {
				//if (content == 1) {
				float scale = (cell.temperature / _MinTemperature); // 0 cel = 0; - 30 cel = 1 
					_Color.r += lerp(0.5f, -0.5f, scale) / blend;
					_Color.g += 0.0f;
					_Color.b += lerp(0.0f, 0.5f, scale) / blend;
					_Color.a = 1.0f;
				//}
				//else {
				//	_Color = float4(0, 0, 0, 1);
				//}
			}

			if (_Show_Pressure) {
				float3 pressure = (cell.pressure);
				float vertical_scale = max(abs(pressure.y) - abs(cell.hardness), 0.0f) / (float) abs(cell.hardness) /*((float) cell.massOver * _MaxSnowDensity / ( _CellSize * _CellSize))*/;
				_Color.r = 0.0f;
				_Color.g = lerp(0.0f, 1.0f, vertical_scale) * (pressure.y / abs(pressure.y));
				_Color.b = 0.0f;

				_Color.a =  saturate(10.0f * abs(length(pressure)));
			}
			
			if (_Show_Indexes) {
				float3 index = ((float3) cell.gridIndex/ (float) 50.0f);
				_Color.r = index.x;
				_Color.g = index.y;
				_Color.b = index.z;

				_Color.a = 0.5f;
			}
		#endif
	}

	 void surf(Input IN, inout SurfaceOutputStandard o) {
		o.Metallic = 0;
		o.Smoothness = 0;
		o.Albedo = _Color.rgb;
		o.Alpha = _Color.a;
	  }

	  ENDCG
	}
		//FallBack "Diffuse"
}
