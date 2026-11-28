//
//  OutlineFill.shader
//  QuickOutline — URP-версия
//
//  Почему пришлось переписать: оригинальные шейдеры Quick Outline написаны под
//  built-in pipeline (CGPROGRAM / UnityCG.cginc). URP такие проходы не рисует,
//  поэтому контур не появлялся ни на одном объекте (материалы добавлялись,
//  но пикселей не было).
//
//  Здесь тот же приём «кокон»: геометрия расширяется по нормалям и рисуется
//  только там, где она выходит за силуэт объекта (Cull Front + ZTest GEqual —
//  в URP включён reversed-Z, поэтому «позади» это GEqual).
//

Shader "Custom/Outline Fill" {
  Properties {
    [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 4

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

      Cull Front
      ZTest GEqual
      ZWrite Off
      Blend SrcAlpha OneMinusSrcAlpha
      ColorMask RGB

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

      // URP: свойства материала обязаны быть в UnityPerMaterial, иначе не биндятся
      // (без этого _OutlineColor приходил нулевым и контур был прозрачным).
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

        // 0.01 — «сантиметр на единицу ширины»: width 4 даёт ~4 см обводки.
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
