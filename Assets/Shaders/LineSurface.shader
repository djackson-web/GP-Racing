Shader "Custom/LineSurface"
{
    Properties
    {
        _Colour     ("Line Colour",   Color)        = (1,1,1,1)
        _Sharpness  ("Edge Sharpness", Range(1, 20)) = 8
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float  alpha       : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Colour;
                float  _Sharpness;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = IN.uv;
                OUT.alpha       = IN.color.a; // already inverted in LineMeshBuilder
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Sharpen the transition so lines pop on/off cleanly
                float alpha = saturate(pow(IN.alpha, _Sharpness));
                return half4(_Colour.rgb, alpha);
            }
            ENDHLSL
        }
    }
}