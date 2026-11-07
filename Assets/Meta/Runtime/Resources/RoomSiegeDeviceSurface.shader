Shader "RoomSiege/DeviceSurface"
{
    Properties
    {
        _Color("Body",Color)=(.3,.4,.5,1)
        _Emission("Core emission",Color)=(0,0,0,1)
        _Metallic("Metal finish",Range(0,1))=.5
    }
    SubShader
    {
        Tags {"Queue"="Geometry" "RenderType"="Opaque"}
        Pass
        {
            ZWrite On
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata {float4 vertex:POSITION;float3 normal:NORMAL;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct v2f {float4 vertex:SV_POSITION;float3 normal:TEXCOORD0;float3 world:TEXCOORD1;UNITY_VERTEX_OUTPUT_STEREO};
            float4 _Color,_Emission;float _Metallic;
            v2f vert(appdata v)
            {
                v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex=UnityObjectToClipPos(v.vertex);o.normal=UnityObjectToWorldNormal(v.normal);
                o.world=mul(unity_ObjectToWorld,v.vertex).xyz;return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n=normalize(i.normal),v=normalize(UnityWorldSpaceViewDir(i.world));
                float3 l=normalize(float3(-.45,.8,-.55)),fill=normalize(float3(.6,.25,.5));
                float diffuse=.38+.55*saturate(dot(n,l))+.22*saturate(dot(n,fill));
                float spec=pow(saturate(dot(n,normalize(l+v))),36)*(.25+.6*_Metallic);
                float rim=pow(1-saturate(dot(n,v)),3)*.18;
                return fixed4(_Color.rgb*diffuse+spec*float3(.8,.92,1)+rim*float3(.25,.65,.85)+_Emission.rgb,1);
            }
            ENDCG
        }
    }
}
