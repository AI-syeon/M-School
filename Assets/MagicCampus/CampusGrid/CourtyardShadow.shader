Shader "Hidden/MagicCampus/CourtyardShadow"
{
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment shade
            #include "UnityCG.cginc"

            struct ShadowVertex
            {
                float4 position : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct ShadowFragment
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            ShadowFragment vert(ShadowVertex input)
            {
                ShadowFragment output;
                output.position = UnityObjectToClipPos(input.position);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            fixed4 shade(ShadowFragment input) : SV_Target
            {
                float radius = length(input.uv);
                float opacity = 1 - smoothstep(0.40, 1.0, radius);
                return fixed4(0.22, 0.15, 0.16, opacity * input.color.a);
            }
            ENDCG
        }
    }
}
