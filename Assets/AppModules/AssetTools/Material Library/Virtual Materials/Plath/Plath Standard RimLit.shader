﻿Shader "Virtual Materials/Plath Standard RimLit" {
	Properties {
    // Plath parameters
    [NoScaleOffset]
    _ProximityGradient ("Proximity Gradient", 2D) = "white" {}
    _ProximityMapping ("Map: DistMin, DistMax, GradMin, GradMax", Vector) = (0, 0.04, 1, 0)

    // Standard parameters
		_MainTex ("Albedo (RGB)", 2D) = "white" {}
		_Glossiness ("Smoothness", Range(0,1)) = 0.5
		_Metallic ("Metallic", Range(0,1)) = 0.0
    _RimColor ("Rim Color", Color) = (1, 1, 1, 1)
	}
	SubShader {
		Tags { "RenderType"="Opaque" }
		LOD 200

		Stencil{
			Ref[_PortalMask]
			ReadMask 3
			Comp equal
		}
		
		CGPROGRAM
     
    #include "Assets/AppModules/TodoUMward/Shader Hand Data/Resources/HandData.cginc"

		// Physically based Standard lighting model, and enable shadows on all light types
		#pragma surface surf Standard fullforwardshadows

		// Use shader model 3.0 target, to get nicer looking lighting
		#pragma target 3.0

		sampler2D _MainTex;

		struct Input {
			float2 uv_MainTex;
      float3 worldPos;
      float3 worldNormal;
      float3 viewDir;
		};
    
    sampler2D _ProximityGradient;
		float4 _ProximityMapping;

		half _Glossiness;
		half _Metallic;
    half4 _RimColor;

		// Add instancing support for this shader. You need to check 'Enable Instancing' on materials that use the shader.
		// See https://docs.unity3d.com/Manual/GPUInstancing.html for more information about instancing.
		// #pragma instancing_options assumeuniformscaling
		UNITY_INSTANCING_BUFFER_START(Props)
			// put more per-instance properties here
		UNITY_INSTANCING_BUFFER_END(Props)

		void surf (Input IN, inout SurfaceOutputStandard o) {
			// Albedo comes from a texture tinted by color
			fixed4 c = tex2D (_MainTex, IN.uv_MainTex);
			o.Albedo = c.rgb;

      // Proximity effect from Plath.
      o.Albedo *= evalProximityColor(IN.worldPos, _ProximityGradient, _ProximityMapping);

      // Rim lighting.
      float toCameraAmount = 1 - dot(IN.worldNormal, IN.viewDir);
      toCameraAmount *= 1.1;
      toCameraAmount *= toCameraAmount;
      toCameraAmount = saturate(toCameraAmount);
      o.Emission = _RimColor.rgb * _RimColor.a * toCameraAmount;

			// Metallic and smoothness come from slider variables
			o.Metallic = _Metallic;
			o.Smoothness = _Glossiness;
			o.Alpha = c.a;
		}
		ENDCG
	}
	//FallBack "Diffuse"
}
