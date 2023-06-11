Shader "Unlit/snow"
{
    Properties
    {
        _SnowHeightMap("Texture", 2D) = "white" {}
         _GroundHeightMap("Texture", 2D) = "white" {}
        _OffsetX("Test X Offset", Range(0,100)) = 10
        _SnowFactorMax("Snow Maxx Height Const", Range(0,100)) = 10
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        Cull OFF

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 position : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
               // float snowHeight : TEXCOORD1;
            };

            sampler2D _GroundHeightMap;
            float4 _GroundHeightMap_ST;
            float _OffsetX;

            struct ColumnData
            {
                float height;
                float mass;
            };

            StructuredBuffer<ColumnData> snowTotalsBuffer;

            v2f vert (appdata v)
            {
                v2f o;
                int _texResolution = 1024;
                //uint index = (uint) floor((0.9-v.uv.x) * _texResolution) + floor((0.9 -v.uv.y) * _texResolution) *_texResolution;


                //uint index = (uint) round(saturate(max(v.uv.x, 0.15)) *( _texResolution-1)) + round(saturate(min(v.uv.y, 0.85)) * (_texResolution-1)) *_texResolution;
                uint index = (uint) round(v.uv.x *( _texResolution-1)) + round(v.uv.y * (_texResolution-1)) *_texResolution;
                //o.snowHeight = snowTotalsBufferOut[index].height;
                float snowHeight = snowTotalsBuffer[index].height;
                float groundHeight = tex2Dlod(_GroundHeightMap, float4(v.uv, 0.0, 0.0)).x;

                v.position.y += groundHeight + snowHeight;
                o.position = UnityObjectToClipPos(v.position);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
               // fixed4 col = tex2D(_GroundHeightMap, i.uv);
                //fixed4 col = fixed4(i.uv,0,1);
                fixed4 col = fixed4(1,1,1,1);
                
                return col;
            }
            ENDCG
        }
    }
}
