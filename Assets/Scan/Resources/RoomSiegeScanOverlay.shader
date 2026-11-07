Shader "RoomSiege/ScanOverlay"
{
    Properties
    {
        _Color("Color", Color) = (1,1,1,1)
        [Toggle] _ZWrite("Write Depth", Float) = 0
        _ClipSafeCells("Clip safety cells", Float) = 0
        _SafeCenter("Core centre, half cell, radius", Vector) = (0,0,0.2,0.16)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_ZWrite]
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 cellCenter : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 cellCenter : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            fixed4 _Color;
            float _ClipSafeCells;
            float4 _SafeCenter;
            v2f vert(appdata v) {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f,o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.cellCenter = v.cellCenter;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                if(_ClipSafeCells>0.5)
                {
                    float2 outside=max(abs(i.cellCenter-_SafeCenter.xy)-_SafeCenter.z,0.0);
                    clip(dot(outside,outside)-_SafeCenter.w*_SafeCenter.w);
                }
                return i.color;
            }
            ENDHLSL
        }
    }
}
