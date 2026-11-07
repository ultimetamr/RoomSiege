Shader "RoomSiege/PreviewSurface"
{
    Properties
    {
        _Color("Tint",Color)=(.31,.85,1,1)
        _RimColor("Edge",Color)=(.31,.85,1,1)
    }
    SubShader
    {
        Tags {"Queue"="Geometry" "RenderType"="Opaque"}
        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata {float4 vertex:POSITION;float3 normal:NORMAL;fixed4 color:COLOR;};
            struct v2f {float4 vertex:SV_POSITION;float3 normal:TEXCOORD0;fixed4 color:COLOR;};
            fixed4 _Color,_RimColor;
            v2f vert(appdata v)
            {
                v2f o;o.vertex=UnityObjectToClipPos(v.vertex);
                o.normal=UnityObjectToWorldNormal(v.normal);o.color=v.color;return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                float light=.48+.52*abs(dot(normalize(i.normal),normalize(float3(.4,.8,.6))));
                fixed3 body=_Color.rgb*lerp(fixed3(.65,.76,.82),i.color.rgb,.5)*light;
                return fixed4(lerp(body,_RimColor.rgb,1-step(.5,i.color.a)),1);
            }
            ENDCG
        }
    }
}
