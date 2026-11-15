Shader "MagicCampus/CourtyardGround"
{
    Properties
    {
        [PerRendererData] _MainTex ("Tile texture", 2D) = "white" {}
        [NoScaleOffset] _GroundNormal ("Ground normal (RGB)", 2D) = "bump" {}
        _GroundSize ("Ground size in cells", Vector) = (1, 1, 0, 0)
        _NormalLight ("Sun direction and intensity", Vector) = (0, -0.423, 0.906, 1)
        _UseSpriteUV ("Sample normals at sprite UVs", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Cull Off ZWrite Off Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex, _GroundNormal;
            float2 _GroundSize;
            float4 _NormalLight;
            float _UseSpriteUV;

            struct GroundFragment
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 normalUV : TEXCOORD1;
                fixed4 color : COLOR;
            };

            GroundFragment vert(float4 position : POSITION, float2 uv : TEXCOORD0, fixed4 color : COLOR)
            {
                GroundFragment output;
                output.position = UnityObjectToClipPos(position);
                output.uv = uv;
                output.normalUV = lerp(mul(unity_ObjectToWorld, position).xy / _GroundSize, uv, _UseSpriteUV);
                output.color = color;
                return output;
            }

            fixed4 frag(GroundFragment input) : SV_Target
            {
                fixed4 albedo = tex2D(_MainTex, input.uv) * input.color;
                float3 encoded = tex2D(_GroundNormal, input.normalUV).rgb;
                float3 normal = normalize(float3((encoded.xy * 255 - 128) / 127, encoded.z * 2 - 1));
                float relief = dot(normal, _NormalLight.xyz) - _NormalLight.z;
                float lighting = 1 + clamp(relief * lerp(0.70, 0.45, _UseSpriteUV), -0.34, 0.27) * _NormalLight.w;
                return fixed4(albedo.rgb * (albedo.a * lighting), albedo.a);
            }
            ENDCG
        }
    }
}
