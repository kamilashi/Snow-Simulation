Shader "Unlit/Snow"
{
    Properties
    {
        _SnowSurfaceColor("Snow Surface Color", Color) = (1,1,1,1)
        _SnowDepthColor("Snow Depth Color", Color) = (0,0,0,1)
        _GroundHeightMap("Texture", 2D) = "white" {}
       [HideInInspector] _SnowMaxHeight("Snow Max Height Const", Range(0,10)) = 10
       [HideInInspector] _TexResolution("Texture Map Resolution", Integer) = 1024
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
                float snowIndent : TEXCOORD1;
            };

            sampler2D _GroundHeightMap;
            float4 _GroundHeightMap_ST;
            float4 _SnowDepthColor;
            float4 _SnowSurfaceColor;
            float _SnowMaxHeight;
            int _TexResolution;

            struct ColumnData
            {
                float height;
                float groundHeight;
                float mass;
                float mass_temp;                
            };

            StructuredBuffer<ColumnData> snowTotalsBuffer;

            v2f vert (appdata v)
            {
                v2f o;
                int _texResolution = _TexResolution;
                uint index = (uint) round(v.uv.x *( _texResolution-1)) + round(v.uv.y * (_texResolution-1)) *_texResolution;
                float snowHeight = snowTotalsBuffer[index].height;
                float groundHeight = tex2Dlod(_GroundHeightMap, float4(v.uv, 0.0, 0.0)).x;

                v.position.y += groundHeight + snowHeight;
                o.snowIndent = 1 - smoothstep(0.45, 1,saturate(snowHeight / _SnowMaxHeight));
                o.position = UnityObjectToClipPos(v.position);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float4 color = _SnowSurfaceColor;
                color = lerp(color, _SnowDepthColor, i.snowIndent);
                
                return color;
            }
            ENDCG
        }
    }
}
