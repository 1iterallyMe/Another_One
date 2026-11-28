//
//  OutlineMask.shader
//  QuickOutline (URP port)
//
//  Original: Chris Nolet (built-in pipeline).
//  Порт на URP: тот же проход-маска, но на HLSLPROGRAM и с тегом UniversalPipeline,
//  иначе URP полностью пропускает built-in шейдер и контур не рисуется.
//

Shader "Custom/Outline Mask" {
  Properties {
    [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 0
  }

  SubShader {
    Tags {
      "Queue" = "Transparent+100"
      "RenderType" = "Transparent"
      "RenderPipeline" = "UniversalPipeline"
    }

    Pass {
      Name "Mask"
      Tags { "LightMode" = "UniversalForward" }

      Cull Off
      ZTest Always
      ZWrite Off
      ColorMask 0

      Stencil {
        Ref 1
        Pass Replace
      }

      HLSLPROGRAM
      #pragma vertex vert
      #pragma fragment frag

      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

      struct Attributes {
        float4 positionOS : POSITION;
      };

      struct Varyings {
        float4 positionCS : SV_POSITION;
      };

      Varyings vert(Attributes input) {
        Varyings output;
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        return output;
      }

      half4 frag(Varyings input) : SV_Target {
        return half4(0, 0, 0, 0);
      }
      ENDHLSL
    }
  }
}
