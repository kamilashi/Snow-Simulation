Shader "Custom/snowParticleShader"
{
	Properties
	{
		_Color("Color", Color) = (1,1,1,1)
		_MainTex("Albedo (RGB)", 2D) = "white" {}/*
		_Glossiness("Smoothness", Range(0,1)) = 0.5
		_Metallic("Metallic", Range(0,1)) = 0.0*/
	}
		SubShader
		{
			Tags { "RenderType" = "Opaque" }
			LOD 200
			Cull Off
			ZWrite On

			CGPROGRAM
			#pragma surface surf Standard vertex:vert addshadow fullforwardshadows
			#pragma instancing_options procedural:setup
			#pragma target 3.0

			sampler2D _MainTex;

			struct Input
			{
				float2 uv_MainTex;
			};

			//half _Glossiness;
			//half _Metallic;
			fixed4 _Color;
			float3 _Position;
			float _ParticleSize;

	#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
			struct Particle
			{
				float3 position;
				float3 velocity;
				float size;/*
				float3 force;
				float3 localPosition;
				float3 offsetPosition;*/
			};

			StructuredBuffer<Particle> particleBuffer;
	#endif


			void vert(inout appdata_full v, out Input data)
			{
				UNITY_INITIALIZE_OUTPUT(Input, data);

	#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
				v.vertex.xyz *= _ParticleSize;
				v.vertex.xyz += _Position;
	#endif
			}

			void setup()
			{
	#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
				Particle particle = particleBuffer[unity_InstanceID];
				_Position = particle.position.xyz;
				_ParticleSize = particle.size;
				//_Position = float3(0, 100, 0);
				
	#endif
			}

			void surf(Input IN, inout SurfaceOutputStandard o)
			{
				// Albedo comes from a texture tinted by color
				// Metallic and smoothness come from slider variables
				o.Metallic = 0;
				o.Smoothness = 1;
				o.Albedo = _Position;
				o.Alpha = _Color.a;
			}
			ENDCG
		}
			FallBack "Diffuse"
}
