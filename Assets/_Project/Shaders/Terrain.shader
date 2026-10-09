// Terrain: five tiling ground materials (grass, dirt, sand, rock, snow) blended by per-vertex weights from
// TerrainSampler.GroundLayers, over a per-chunk biome tint (_BaseMap, set by ChunkStreamer).
// Each layer's photo is split into detail (photo / its average colour) and colour; _<Layer>Photo picks how much
// of the photo's own colour to keep versus the biome tint. Rock is projected from three sides so cliffs do not
// stretch. Texture UVs are world metres / tile size, offset by the chunk origin so they continue across chunks:
// keep chunk size / (tile size x anti-repeat multiplier) a whole number or chunk edges show seams.
Shader "EpochsUnbound/Terrain"
{
    Properties
    {
        [NoScaleOffset] _BaseMap("Biome Tint (per chunk, set at runtime)", 2D) = "white" {}

        [NoScaleOffset] _GrassAlbedo("Grass Albedo", 2D) = "grey" {}
        [NoScaleOffset][Normal] _GrassNormal("Grass Normal", 2D) = "bump" {}
        [NoScaleOffset] _GrassRough("Grass Roughness", 2D) = "white" {}
        _GrassTiling("Grass Tile Size (m)", Float) = 4
        _GrassPhoto("Grass Photo Colour", Range(0, 1)) = 0

        [NoScaleOffset] _DirtAlbedo("Dirt Albedo", 2D) = "grey" {}
        [NoScaleOffset][Normal] _DirtNormal("Dirt Normal", 2D) = "bump" {}
        [NoScaleOffset] _DirtRough("Dirt Roughness", 2D) = "white" {}
        _DirtTiling("Dirt Tile Size (m)", Float) = 4
        _DirtPhoto("Dirt Photo Colour", Range(0, 1)) = 0

        [NoScaleOffset] _SandAlbedo("Sand Albedo", 2D) = "grey" {}
        [NoScaleOffset][Normal] _SandNormal("Sand Normal", 2D) = "bump" {}
        [NoScaleOffset] _SandRough("Sand Roughness", 2D) = "white" {}
        _SandTiling("Sand Tile Size (m)", Float) = 4
        _SandPhoto("Sand Photo Colour", Range(0, 1)) = 0

        [NoScaleOffset] _RockAlbedo("Rock Albedo", 2D) = "grey" {}
        [NoScaleOffset][Normal] _RockNormal("Rock Normal", 2D) = "bump" {}
        [NoScaleOffset] _RockRough("Rock Roughness", 2D) = "white" {}
        _RockTiling("Rock Tile Size (m)", Float) = 16
        _RockPhoto("Rock Photo Colour", Range(0, 1)) = 0

        [NoScaleOffset] _SnowAlbedo("Snow Albedo", 2D) = "grey" {}
        [NoScaleOffset][Normal] _SnowNormal("Snow Normal", 2D) = "bump" {}
        [NoScaleOffset] _SnowRough("Snow Roughness", 2D) = "white" {}
        _SnowTiling("Snow Tile Size (m)", Float) = 8
        _SnowPhoto("Snow Photo Colour", Range(0, 1)) = 0

        _NormalStrength("Normal Strength", Range(0, 2)) = 1
        _FarTiling("Anti-repeat Tile Multiplier", Float) = 4
        _BlendSharpness("Layer Blend Sharpness", Range(1, 8)) = 3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _GrassTiling, _DirtTiling, _SandTiling, _RockTiling, _SnowTiling;
            half _GrassPhoto, _DirtPhoto, _SandPhoto, _RockPhoto, _SnowPhoto;
            half _NormalStrength, _BlendSharpness;
            float _FarTiling;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            // One shared repeat sampler for every ground texture.
            SAMPLER(sampler_GrassAlbedo);
            TEXTURE2D(_GrassAlbedo); TEXTURE2D(_GrassNormal); TEXTURE2D(_GrassRough);
            TEXTURE2D(_DirtAlbedo);  TEXTURE2D(_DirtNormal);  TEXTURE2D(_DirtRough);
            TEXTURE2D(_SandAlbedo);  TEXTURE2D(_SandNormal);  TEXTURE2D(_SandRough);
            TEXTURE2D(_RockAlbedo);  TEXTURE2D(_RockNormal);  TEXTURE2D(_RockRough);
            TEXTURE2D(_SnowAlbedo);  TEXTURE2D(_SnowNormal);  TEXTURE2D(_SnowRough);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 weights : COLOR;     // r rock, g sand, b snow, a dirt
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                half3 normalWS : TEXCOORD3;
                half4 weights : TEXCOORD4;
                half fogFactor : TEXCOORD5;
            };

            struct Layer { half3 albedo; half3 normalWS; half smoothness; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.positionOS = v.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.weights = v.weights;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // Chunk origins are whole metres, so frac(origin / tile) is exact even far from the world origin, and
            // local positions stay small: UVs keep full precision anywhere in an endless world.
            float2 TileUV(float2 originXZ, float2 localXZ, float tile)
            {
                return frac(originXZ / tile) + localXZ / tile;
            }

            // Photo split into colour-neutral detail and colour; mixes the biome tint back in by (1 - photo).
            // uvFar samples the same photo at a larger tile size.
            half3 LayerColour(TEXTURE2D_PARAM(albedoMap, smp), float2 uv, float2 uvFar, half3 tint, half photo)
            {
                half3 avg = max(SAMPLE_TEXTURE2D_LOD(albedoMap, smp, float2(0.5, 0.5), 13).rgb, half3(0.02, 0.02, 0.02));
                half3 near = SAMPLE_TEXTURE2D(albedoMap, smp, uv).rgb;
                half3 far = SAMPLE_TEXTURE2D(albedoMap, smp, uvFar).rgb;
                half3 albedo = near * lerp(1.0, far / avg, 0.5);   // larger-scale variation breaks up repeats
                return lerp(tint * albedo / avg, albedo, photo);
            }

            // Top-down layer. Whiteout blend of the tangent normal onto the surface normal.
            Layer TopLayer(TEXTURE2D_PARAM(albedoMap, smp), TEXTURE2D(normalMap), TEXTURE2D(roughMap),
                           float2 originXZ, float2 localXZ, float tile, half3 tint, half photo, half3 n)
            {
                float2 uv = TileUV(originXZ, localXZ, tile);
                Layer l;
                l.albedo = LayerColour(TEXTURE2D_ARGS(albedoMap, smp), uv, TileUV(originXZ, localXZ, tile * _FarTiling), tint, photo);
                half3 t = UnpackNormalScale(SAMPLE_TEXTURE2D(normalMap, smp, uv), _NormalStrength);
                l.normalWS = normalize(half3(t.x + n.x, abs(t.z) * n.y, t.y + n.z));
                l.smoothness = 1.0 - SAMPLE_TEXTURE2D(roughMap, smp, uv).r;
                return l;
            }

            // Three-sided (triplanar) layer for cliffs.
            Layer TriLayer(TEXTURE2D_PARAM(albedoMap, smp), TEXTURE2D(normalMap), TEXTURE2D(roughMap),
                           float3 originWS, float3 localWS, float tile, half3 tint, half photo, half3 n)
            {
                half3 bw = pow(abs(n), 4.0);
                bw /= (bw.x + bw.y + bw.z);
                float2 uvX = TileUV(originWS.zy, localWS.zy, tile);
                float2 uvY = TileUV(originWS.xz, localWS.xz, tile);
                float2 uvZ = TileUV(originWS.xy, localWS.xy, tile);
                float far = tile * _FarTiling;

                Layer l;
                l.albedo = LayerColour(TEXTURE2D_ARGS(albedoMap, smp), uvX, TileUV(originWS.zy, localWS.zy, far), tint, photo) * bw.x
                         + LayerColour(TEXTURE2D_ARGS(albedoMap, smp), uvY, TileUV(originWS.xz, localWS.xz, far), tint, photo) * bw.y
                         + LayerColour(TEXTURE2D_ARGS(albedoMap, smp), uvZ, TileUV(originWS.xy, localWS.xy, far), tint, photo) * bw.z;

                half3 tx = UnpackNormalScale(SAMPLE_TEXTURE2D(normalMap, smp, uvX), _NormalStrength);
                half3 ty = UnpackNormalScale(SAMPLE_TEXTURE2D(normalMap, smp, uvY), _NormalStrength);
                half3 tz = UnpackNormalScale(SAMPLE_TEXTURE2D(normalMap, smp, uvZ), _NormalStrength);
                tx = half3(tx.xy + n.zy, abs(tx.z) * n.x);
                ty = half3(ty.xy + n.xz, abs(ty.z) * n.y);
                tz = half3(tz.xy + n.xy, abs(tz.z) * n.z);
                l.normalWS = normalize(tx.zyx * bw.x + ty.xzy * bw.y + tz.xyz * bw.z);

                l.smoothness = 1.0 - (SAMPLE_TEXTURE2D(roughMap, smp, uvX).r * bw.x
                                    + SAMPLE_TEXTURE2D(roughMap, smp, uvY).r * bw.y
                                    + SAMPLE_TEXTURE2D(roughMap, smp, uvZ).r * bw.z);
                return l;
            }

            void Over(inout Layer base, Layer top, half w)
            {
                base.albedo = lerp(base.albedo, top.albedo, w);
                base.normalWS = lerp(base.normalWS, top.normalWS, w);
                base.smoothness = lerp(base.smoothness, top.smoothness, w);
            }

            half Sharpen(half w)
            {
                // Narrower transitions read as real edges between materials rather than a smear.
                return saturate((w - 0.5) * _BlendSharpness + 0.5);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half3 n = normalize(i.normalWS);
                half3 tint = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                float3 origin = UNITY_MATRIX_M._m03_m13_m23;
                float3 local = i.positionOS;   // chunks are unrotated and unscaled, so this is world offset from the origin
                half4 w = i.weights;

                #define TOP(L) TopLayer(TEXTURE2D_ARGS(_##L##Albedo, sampler_GrassAlbedo), _##L##Normal, _##L##Rough, \
                                        origin.xz, local.xz, _##L##Tiling, tint, _##L##Photo, n)
                Layer g = TOP(Grass);
                Over(g, TOP(Dirt), Sharpen(w.a));
                Over(g, TOP(Sand), Sharpen(w.g));
                Over(g, TriLayer(TEXTURE2D_ARGS(_RockAlbedo, sampler_GrassAlbedo), _RockNormal, _RockRough,
                                 origin, local, _RockTiling, tint, _RockPhoto, n), Sharpen(w.r));
                Over(g, TOP(Snow), Sharpen(w.b));
                #undef TOP

                InputData input = (InputData)0;
                input.positionWS = i.positionWS;
                input.positionCS = i.positionCS;
                input.normalWS = normalize(g.normalWS);
                input.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                input.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                input.fogCoord = i.fogFactor;
                input.bakedGI = SampleSH(input.normalWS);
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                input.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = saturate(g.albedo);
                surface.smoothness = saturate(g.smoothness);
                surface.occlusion = 1;
                surface.alpha = 1;

                half4 colour = UniversalFragmentPBR(input, surface);
                colour.rgb = MixFog(colour.rgb, i.fogFactor);
                return colour;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            float4 Vert(float4 positionOS : POSITION, float3 normalOS : NORMAL) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }

            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half Frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            // Used by screen-space ambient occlusion. Vertex normals only: ground detail is too fine to matter there.
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };

            Varyings Vert(float4 positionOS : POSITION, float3 normalOS : NORMAL)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(normalOS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = saturate(PackNormalOctQuadEncode(n) * 0.5 + 0.5);
                    return half4(PackFloat2To888(oct), 0);
                #else
                    return half4(n, 0);
                #endif
            }
            ENDHLSL
        }
    }
}
