//
//  OutlineFill.shader
//  QuickOutline — URP-версия
//
//  Оригинальные шейдеры ассета написаны под built-in pipeline (CGPROGRAM /
//  UnityCG.cginc) — URP такие проходы не рисует вообще, поэтому контура не было.
//  Здесь та же схема ассета, но на URP:
//    1) проход-маска (OutlineMask) пишет стенсил 1 по силуэту объекта;
//    2) этот проход рисует «кокон», расширенный по нормалям, но только там,
//       где стенсил != 1 — то есть строго за пределами объекта.
//  Так получается именно обводка, а не заливка объекта.
//

Shader "Custom/Outline Fill" {
  Properties {
    [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 0

    _OutlineColor("Outline Color", Color) = (1, 1, 1, 1)
    _OutlineWidth("Outline Width", Range(0, 10)) = 2
  }

  SubShader {
    Tags {
      "Queue" = "Transparent+110"
      "RenderType" = "Transparent"
      "DisableBatching" = "True"
      "RenderPipeline" = "UniversalPipeline"
    }

    Pass {
      Name "Fill"
      Tags { "LightMode" = "UniversalForward" }

      Cull Off
      ZTest Always
      ZWrite Off
      Blend SrcAlpha OneMinusSrcAlpha
      ColorMask RGB

      Stencil {
        Ref 1
        Comp NotEqual
      }

      HLSLPROGRAM
      #pragma vertex vert
      #pragma fragment frag

      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

      struct Attributes {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float3 smoothNormal : TEXCOORD3;
      };

      struct Varyings {
        float4 positionCS : SV_POSITION;
        half4 color : COLOR;
      };

      // URP: свойства материала обязаны быть в UnityPerMaterial, иначе не биндятся.
      CBUFFER_START(UnityPerMaterial)
        half4 _OutlineColor;
        float _OutlineWidth;
      CBUFFER_END

      Varyings vert(Attributes input) {
        Varyings output;

        // UV3 — сглаженные нормали (их пишет Outline.LoadSmoothNormals),
        // иначе берём обычную нормаль вершины.
        float3 normal = any(input.smoothNormal) ? input.smoothNormal : input.normalOS;

        float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
        float3 normalWS = TransformObjectToWorldNormal(normal);

        // 0.01 — «сантиметр на единицу ширины»: width 10 даёт ~10 см кокона.
        positionWS += normalWS * (_OutlineWidth * 0.01);

        output.positionCS = TransformWorldToHClip(positionWS);
        output.color = _OutlineColor;

        return output;
      }

      half4 frag(Varyings input) : SV_Target {
        return input.color;
      }
      ENDHLSL
    }
  }
}
