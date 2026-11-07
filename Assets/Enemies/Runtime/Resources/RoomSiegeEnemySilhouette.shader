Shader "RoomSiege/EnemySilhouette"
{
    Properties
    {
        _Color("Silhouette tint",Color)=(.56,.43,.44,.42)
        _RimColor("Crimson edge",Color)=(1,.35294,.35294,1)
        _HitFlash("Hit highlight",Range(0,1))=0
        _Dissolve("Death dissolve",Range(0,1))=0
        _DetailTex("2K hologram microdetail",2D)="white"{}
    }
    SubShader
    {
        Tags {"Queue"="Transparent" "RenderType"="Transparent"}
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata {float4 vertex:POSITION;float3 normal:NORMAL;float2 uv:TEXCOORD0;fixed4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct v2f {float4 vertex:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float2 uv:TEXCOORD2;fixed4 color:COLOR;UNITY_VERTEX_OUTPUT_STEREO};
            fixed4 _Color,_RimColor;
            float _HitFlash,_Dissolve;
            sampler2D _DetailTex;
            v2f vert(appdata v)
            {
                v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex=UnityObjectToClipPos(v.vertex);o.world=mul(unity_ObjectToWorld,v.vertex).xyz;
                o.normal=UnityObjectToWorldNormal(v.normal);o.uv=v.uv;o.color=v.color;return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float grain=frac(sin(dot(floor(i.world*180),float3(12.9898,78.233,39.425)))*43758.5453);
                clip(grain-_Dissolve+.0001);
                float rim=pow(1-saturate(dot(normalize(i.normal),normalize(UnityWorldSpaceViewDir(i.world)))),2);
                float detail=lerp(.94,1,tex2D(_DetailTex,i.uv).r);
                fixed3 tint=lerp(_Color.rgb,_RimColor.rgb,rim*.85)*i.color.rgb*detail*1.65;
                tint=lerp(tint,fixed3(1,.94,.94),saturate(_HitFlash)*.65);
                float edge=1-step(.5,i.color.a);
                tint=lerp(tint,_RimColor.rgb*1.12,edge);
                return fixed4(tint,saturate(_Color.a*lerp(i.color.a*(.52+rim*.95),2.5,edge)));
            }
            ENDCG
        }
    }
}
