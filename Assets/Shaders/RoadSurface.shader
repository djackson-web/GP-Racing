Shader "Custom/RoadSurface"
{
    Properties
    {
        _MainTex        ("Road Texture",       2D)    = "white" {}
        _KerbFraction   ("Kerb Fraction",      Range(0, 0.2)) = 0.08
        _LineWidth      ("White Line Width",   Range(0, 0.1)) = 0.02
        _LineColour     ("White Line Colour",  Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

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
                float  curvature   : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float  _KerbFraction;
                float  _LineWidth;
                float4 _LineColour;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.curvature   = IN.color.r;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float u = IN.uv.x;

                half4 roadCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);

                bool inLeftEdge  = u < _KerbFraction;
                bool inRightEdge = u > (1.0 - _KerbFraction);
                bool inEdge      = inLeftEdge || inRightEdge;

                bool inLeftLine  = u < _LineWidth;
                bool inRightLine = u > (1.0 - _LineWidth);
                bool inLine      = inLeftLine || inRightLine;

                half4 col = roadCol;

                if (inEdge)
                {
                    half4 straightCol = inLine ? _LineColour : half4(roadCol.rgb, 1);
                    half4 cornerCol   = roadCol;
                    col = lerp(straightCol, cornerCol, IN.curvature);
                }

                return col;
            }
            ENDHLSL
        }
    }
}