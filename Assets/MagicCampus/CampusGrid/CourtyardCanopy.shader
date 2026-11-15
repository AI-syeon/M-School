Shader "Hidden/MagicCampus/CourtyardCanopy"
{
    Properties { _MainTex ("Canopy opacity", 2D) = "white" {} }
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
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float2 _ShadowDirection;
            int _CasterCount;
            float4 _ProjectedCanopies[12];
            float4 _Footprints[12];

            struct CanopyFragment
            {
                float4 position : SV_POSITION;
                float2 world : TEXCOORD0;
            };

            CanopyFragment vert(float4 position : POSITION)
            {
                CanopyFragment output;
                output.position = UnityObjectToClipPos(position);
                output.world = mul(unity_ObjectToWorld, position).xy;
                return output;
            }

            fixed4 shade(CanopyFragment input) : SV_Target
            {
                float2 world = (floor(input.world * 32.0) + 0.5) / 32.0;
                float2 across = float2(_ShadowDirection.y, -_ShadowDirection.x);
                float transmission = 1.0;
                for (int i = 0; i < _CasterCount; i++)
                {
                    float4 canopy = _ProjectedCanopies[i];
                    float2 delta = world - canopy.xy;
                    float2 uv = float2(dot(delta, across), dot(delta, _ShadowDirection)) * _Footprints[i].xy + 0.5;
                    if (any(uv < 0.0) || any(uv > 1.0)) continue;
                    float mask = tex2D(_MainTex, uv).a;
                    transmission *= 1.0 - mask * canopy.z;
                }
                return fixed4(0.25, 0.24, 0.30, 1.0 - transmission);
            }
            ENDCG
        }
    }
}
