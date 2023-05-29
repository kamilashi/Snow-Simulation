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
                float2 uv : TEXCOORD0;
                float4 position : SV_POSITION;
            };

            sampler2D _SnowHeightMap;
            float4 _SnowHeightMap_ST;
            float _SnowFactorMax;
            sampler2D _GroundHeightMap;
            float4 _GroundHeightMap_ST;
            float _OffsetX;

            v2f vert (appdata v)
            {
                v2f o;
                float snowHeight = tex2Dlod(_SnowHeightMap, float4(v.uv, 0.0, 0.0)).x;
                float groundHeight = tex2Dlod(_GroundHeightMap, float4(v.uv, 0.0, 0.0)).x;
                v.position.y += groundHeight + snowHeight *_SnowFactorMax;
                o.position = UnityObjectToClipPos(v.position);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = tex2D(_SnowHeightMap, i.uv)+ (0.2, 0.2, 0.2, 0); //some snow offsetcolorto makeit more white
                
                return col;
            }
            ENDCG
        }
    }
}
