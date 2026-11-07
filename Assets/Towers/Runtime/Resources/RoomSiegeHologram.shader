Shader "RoomSiege/Hologram"
{
    Properties
    {
        _Color("Tint",Color)=(1,1,1,1)
        _DetailTex("2K hologram microdetail",2D)="white"{}
    }
    SubShader
    {
        Tags {"Queue"="Transparent" "RenderType"="Transparent"}
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata {float4 vertex:POSITION;fixed4 color:COLOR;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct v2f {float4 vertex:SV_POSITION;fixed4 color:COLOR;float2 uv:TEXCOORD0;float3 world:TEXCOORD1;UNITY_VERTEX_OUTPUT_STEREO};
            fixed4 _Color;
            sampler2D _DetailTex;
            v2f vert(appdata v)
            {
                v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex=UnityObjectToClipPos(v.vertex);o.color=v.color*_Color;o.uv=v.uv;
                o.world=mul(unity_ObjectToWorld,v.vertex).xyz;return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float edge=saturate(1-abs(i.uv.y*2-1));
                float core=pow(edge,3);
                float detail=lerp(.90,1,tex2D(_DetailTex,i.world.xz+i.world.y*.13).r);
                // Analytic soft glow keeps the line readable without an HDR/bloom camera pass.
                return fixed4(i.color.rgb*(1+core*.65)*detail,i.color.a*(core+edge*.22));
            }
            ENDCG
        }
    }
}
