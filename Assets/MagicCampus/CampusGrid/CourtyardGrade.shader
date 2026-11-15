Shader "Hidden/MagicCampus/CourtyardGrade"
{
    Properties { _MainTex ("Scene", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment grade
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _WorldFrame;
            float4 _Lanterns[8];
            int _LanternCount;
            float4 _Solar; // Ambient daylight, warmth, direct sun, lantern power
            float2 _SunDirection;

            fixed4 grade(v2f_img input) : SV_Target
            {
                float2 uv = input.uv;
                float3 color = tex2D(_MainTex, uv).rgb;
                float3 contrast = saturate((color - 0.42) * 1.04 + 0.42);
                float sunSide = saturate(0.5 + dot(uv - 0.5, _SunDirection) * 0.95);
                float3 warm = lerp(float3(1.02, 1.02, 0.99), float3(1.18, 1.01, 0.75), _Solar.y);
                float3 day = contrast * warm * (0.95 + 0.15 * sunSide * _Solar.z);
                float3 night = color * float3(0.29, 0.37, 0.60) + float3(0.012, 0.014, 0.025);
                float3 lit = lerp(night, day, _Solar.x);
                float edges = smoothstep(0.32, 0.72, length((uv - 0.5) * float2(1.06, 0.85)));
                lit *= lerp(float3(1, 1.005, 1), float3(0.86, 0.85, 0.90), edges);

                float2 world = _WorldFrame.xy + uv * _WorldFrame.zw;
                float glow = 0;
                for (int index = 0; index < _LanternCount; index++)
                {
                    float4 lamp = _Lanterns[index];
                    float2 offset = (world - lamp.xy) / lamp.z;
                    float falloff = saturate(1 - length(offset));
                    glow += falloff * falloff * lamp.w;
                }
                lit += glow * _Solar.w * (float3(0.50, 0.27, 0.065) + color * float3(0.30, 0.16, 0.06));
                return fixed4(saturate(lit), 1);
            }
            ENDCG
        }
    }
}
