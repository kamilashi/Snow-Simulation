Shader "Unlit/Ground"
{
    Properties
    {
       [HideInInspector] _GroundHeightMap("Texture", 2D) = "white" {}
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

            sampler2D _GroundHeightMap;
            float4 _GroundHeightMap_ST;

            v2f vert (appdata v)
            {
                v2f o;
                float height = tex2Dlod(_GroundHeightMap, float4(v.uv, 0.0, 0.0)).x;
                v.position.y += height;
                o.position = UnityObjectToClipPos(v.position);
                o.uv = v.uv;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float4 color = float4(0,0,0,1);
                return color;
            }
            ENDCG
        }
    }
}
