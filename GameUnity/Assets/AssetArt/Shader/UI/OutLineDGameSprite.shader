Shader "DGame/Actor/OutLineDGameSprite"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}

        _Color("Color",Color) = (1,1,1,1)//颜色

        [Header(Gray)]
        _GrayPhase("Phase", Range(0, 1)) = 0

        [Header(HightLight)]
        _HightLightOpen("HightLightOpen", Float) = 0
        _HighlightColor("HighlightColor", Color) = (1,1,1,1)//高亮的颜色
        _HighlightVal("HighlightVal",Range(0, 1)) = 0//高亮的数值

        [Header(OutLine)]
       _EdgeWidth("EdgeWidth", Float) = 0.5
        _OutlineColor("OutLineColor",Color) = (1,1,1,1)//描边颜色
        _EdgeColorInt("EdgeColorInt", Float) = 2
    }
    SubShader
    {
        Tags
        {
            "QUEUE" = "Transparent" "IGNOREPROJECTOR" = "true" "RenderType" = "Transparent"
        }
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        LOD 100
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"


            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            uniform fixed4 _OutlineColor;
            uniform float _EnableExternalAlpha;
            uniform sampler2D _MainTex;
            uniform sampler2D _AlphaTex;
            float4 _MainTex_TexelSize;
            uniform float _EdgeWidth;
            uniform float4 _MainTex_ST;
            uniform float _EdgeColorInt;
            fixed4 _HighlightColor;
             float _HightLightOpen;
            float _HighlightVal; //对外可动态修改高亮数值
            float4 _Color;
            float _GrayPhase;

            v2f vert(appdata_t i)
            {
                v2f o;
                i.vertex.xyz += float3(0, 0, 0);
                o.vertex = UnityObjectToClipPos(i.vertex);
                o.uv = i.uv;
                o.color = i.color * _OutlineColor;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 mainTexSize = (_MainTex_TexelSize.xy * _EdgeWidth);
                float2 mainTexOffset1 = (float2(1.0, 1.0));
                float2 mainTexOffset2 = (float2(1.0, -1.0));
                float2 mainTexOffset3 = (float2(-1.0, 1.0));
                float2 mainTexOffset4 = (float2(-1.0, -1.0));
                float wholeA = saturate((tex2D(_MainTex, (i.uv + (mainTexSize * mainTexOffset1))).a
                    + tex2D(_MainTex, (i.uv + (mainTexSize * mainTexOffset2))).a
                    + tex2D(_MainTex, (i.uv + (mainTexSize * mainTexOffset3))).a
                    + tex2D(_MainTex, (i.uv + (mainTexSize * mainTexOffset4))).a));
                float4 finallyCol = float4((i.color * wholeA * _EdgeColorInt).rgb, wholeA);
                
                fixed4 tex = tex2D(_MainTex, i.uv);
                tex.rgb = (tex.rgb + _HighlightColor.rgb * _HighlightVal * step(1, _HightLightOpen));
                tex.rgb = lerp(tex.rgb, dot(tex.rgb, float3(0.2125, 0.59, 0.11)), _GrayPhase);
                tex.rgba = tex.rgba * _Color.rgba;
                
                float4 finalCol = lerp(finallyCol, tex, tex.a);
                return finalCol;
            }
            ENDCG
        }
    }
}
