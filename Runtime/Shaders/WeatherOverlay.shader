// Copyright 2026 Ayase Minori and Bright Labs
// Licensed under the BSD-3-Clause License
// See LICENSE for details
Shader "Nakayama/WeatherOverlay"
{
    Properties
    {
        // Name differs from _UdonWeatherParams so material slots never shadow runtime globals.
        _PreviewWeatherParams ("Editor Preview Params (Mode, Alpha, Wind, Speed)", Vector) = (0, 0, 0, 1)
    }
    SubShader
    {
        Tags
        {
            "Queue"="Transparent+400"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "DisableBatching"="True"
        }
        Cull Front
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma exclude_renderers d3d11_9x gles

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                nointerpolation float4 weatherParams : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // x=mode (0 None, 1 Rain, 2 Snow, 3 Storm, 4 Typhoon, 5 Arid), y=opacity, z=wind slant, w=speed.
            float4 _UdonWeatherParams;
            float4 _PreviewWeatherParams;
            float4 _UdonSunDirection;

            #define OVERLAY_DOME_RADIUS 400.0f
            #define OVERLAY_GUST_CYCLES 3.0f

            float4 Hash24(float2 p)
            {
                float4 p4 = frac(float4(p.xyxy) * float4(0.1031f, 0.1030f, 0.0973f, 0.1099f));
                p4 += dot(p4, p4.wzxy + 33.33f);
                return frac((p4.xxyz + p4.yzzw) * p4.zywx);
            }

            float EvaluateRainShell(
                float2 angularUv,
                float timeSec,
                float2 gridScale,
                float windSlant,
                float fallSpeed,
                float streakWidth,
                float streakLength)
            {
                // Integer x cell count avoids a seam across the atan2 wrap meridian.
                float2 cellScale = float2(round(gridScale.x), gridScale.y);

                float2 uv = angularUv;
                uv.x += uv.y * windSlant;
                uv *= cellScale;
                uv.y -= timeSec * fallSpeed;

                float2 cellId = floor(uv);
                float2 cellUv = frac(uv) - 0.5f;

                float4 rnd = Hash24(cellId);
                float2 jitter = rnd.xy - 0.5f;
                float2 delta = cellUv - float2(jitter.x * 0.65f, jitter.y * 0.35f);

                // Clamp drop length within the cell so frac() never slices a streak mid-fall.
                float lengthVar = 0.70f + 0.60f * rnd.z;
                float len = min(streakLength * lengthVar, 0.5f - abs(jitter.y * 0.35f));

                float xMask = 1.0f - smoothstep(0.0f, streakWidth, abs(delta.x));
                float yMask = 1.0f - smoothstep(0.0f, len, abs(delta.y));
                float headFade = saturate((delta.y + len) / (2.0f * len));
                float dropSeed = step(0.32f, rnd.w);

                return xMask * yMask * (0.45f + 0.55f * headFade) * dropSeed;
            }

            float EvaluateParticleShell(
                float2 angularUv,
                float timeSec,
                float2 gridScale,
                float windDrift,
                float fallSpeed,
                float radius)
            {
                float2 uv = angularUv * float2(round(gridScale.x), gridScale.y);
                uv.y -= timeSec * fallSpeed;
                uv.x += timeSec * windDrift + sin(timeSec * 1.4f + uv.y * 0.45f) * 0.25f;

                float2 cellId = floor(uv);
                float2 cellUv = frac(uv) - 0.5f;

                float4 rnd = Hash24(cellId);
                float2 jitter = (rnd.xy - 0.5f) * 0.55f;
                float dist = length(cellUv - jitter);
                float seed = step(0.42f, rnd.z);

                return (1.0f - smoothstep(radius * 0.25f, radius, dist)) * seed;
            }

            fixed4 EvaluateArid(float2 angularUv, float3 viewDir, float horizonEnvelope, float t, float wind, float speed, float alphaWeight)
            {
                float moteMask = 0.0f;
                if (horizonEnvelope > 0.0f)
                {
                    float dustNear = EvaluateParticleShell(angularUv, t, float2(30.0f, 12.0f), wind, -speed * 0.5f, 0.040f);
                    float dustFar = EvaluateParticleShell(angularUv + 0.37f, t * 0.8f, float2(52.0f, 20.0f), wind * 0.7f, -speed * 0.35f, 0.030f);
                    moteMask = (dustNear * 0.55f + dustFar * 0.35f) * horizonEnvelope;
                }

                float haze = saturate(1.0f - abs(viewDir.y) * 2.2f);
                float horizonHaze = haze * haze * 0.14f;

                // Gate sunlit dust and warm haze by sun elevation so they vanish at night.
                float sunLift = saturate(_UdonSunDirection.y * 3.0f + 0.25f);
                float aridMask = (moteMask + horizonHaze) * sunLift * alphaWeight;
                return fixed4(0.95f, 0.84f, 0.64f, saturate(aridMask * 0.55f));
            }

            fixed4 EvaluateSnow(float2 angularUv, float horizonEnvelope, float t, float wind, float speed, float alphaWeight)
            {
                float snowMask = 0.0f;
                if (horizonEnvelope > 0.0f)
                {
                    float s1 = EvaluateParticleShell(angularUv, t, float2(30.0f, 10.0f), wind, speed * 2.4f, 0.055f);
                    float s2 = EvaluateParticleShell(angularUv + 0.23f, t, float2(52.0f, 16.0f), wind * 0.8f, speed * 3.1f, 0.045f);
                    float s3 = EvaluateParticleShell(angularUv + 0.61f, t, float2(78.0f, 24.0f), wind * 0.6f, speed * 3.8f, 0.036f);
                    snowMask = (s1 * 0.50f + s2 * 0.35f + s3 * 0.25f) * horizonEnvelope * alphaWeight;
                }

                return fixed4(0.94f, 0.97f, 1.0f, saturate(snowMask * 0.78f));
            }

            fixed4 EvaluateRain(float2 angularUv, float3 viewDir, float azimuth, float horizonEnvelope, float mode, float t, float wind, float speed, float alphaWeight)
            {
                float densityScale = (mode > 3.5f) ? 1.55f : ((mode > 2.5f) ? 1.25f : 1.0f);

                float shellWeight = 0.0f;
                if (horizonEnvelope > 0.0f)
                {
                    float r1 = EvaluateRainShell(angularUv, t, float2(64.0f, 11.0f) * densityScale, wind, speed * 8.5f, 0.030f, 0.42f);
                    float r2 = EvaluateRainShell(angularUv + 0.31f, t, float2(104.0f, 16.0f) * densityScale, wind * 0.9f, speed * 11.0f, 0.024f, 0.46f);
                    float r3 = EvaluateRainShell(angularUv + 0.67f, t, float2(150.0f, 22.0f) * densityScale, wind * 0.8f, speed * 13.5f, 0.018f, 0.50f);
                    shellWeight = r1 * 0.50f + r2 * 0.35f + r3 * 0.25f;
                }

                float mistVeil = 0.0f;
                if (mode > 2.5f)
                {
                    float gustWave = 0.5f + 0.5f * sin(azimuth * UNITY_TWO_PI * OVERLAY_GUST_CYCLES + t * 5.0f);
                    float horizonBand = saturate(1.0f - abs(viewDir.y) * 1.65f);
                    mistVeil = horizonBand * ((mode > 3.5f) ? (0.18f + 0.08f * gustWave) : 0.08f);
                }

                float rainMask = (shellWeight * horizonEnvelope + mistVeil) * alphaWeight;
                float3 rainColor = (mode > 3.5f) ? float3(0.76f, 0.83f, 0.90f) : float3(0.82f, 0.88f, 0.95f);
                return fixed4(rainColor, saturate(rainMask * 0.72f));
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                bool useUdon = dot(_UdonWeatherParams, _UdonWeatherParams) > 1e-6f;
                float4 wp = useUdon ? _UdonWeatherParams : _PreviewWeatherParams;

                // Push inactive triangles outside NDC with valid w=1 so zero fragments run.
                if (wp.x < 0.5f || wp.y <= 0.001f)
                {
                    o.vertex = float4(2.0f, 2.0f, 0.0f, 1.0f);
                    o.worldPos = float3(0.0f, 0.0f, 0.0f);
                    o.weatherParams = float4(0.0f, 0.0f, 0.0f, 0.0f);
                    return o;
                }

                float3 worldDir = UnityObjectToWorldDir(v.vertex.xyz);
                float3 domePos = _WorldSpaceCameraPos + worldDir * OVERLAY_DOME_RADIUS;

                o.vertex = UnityWorldToClipPos(domePos);
                o.worldPos = domePos;
                o.weatherParams = wp;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float mode = i.weatherParams.x;
                float alphaWeight = saturate(i.weatherParams.y);

                float3 viewDir = normalize(i.worldPos - _WorldSpaceCameraPos);
                float azimuth = atan2(viewDir.x, viewDir.z) * UNITY_INV_TWO_PI;
                float elevation = asin(clamp(viewDir.y, -1.0f, 1.0f)) * (2.0f * UNITY_INV_PI);
                float2 angularUv = float2(azimuth, -elevation);

                float zenithFade = 1.0f - smoothstep(0.55f, 0.98f, viewDir.y);
                float groundFade = smoothstep(-0.55f, -0.12f, viewDir.y);
                float horizonEnvelope = zenithFade * groundFade;

                float t = _Time.y;
                float wind = i.weatherParams.z;
                float speed = i.weatherParams.w;

                UNITY_BRANCH
                if (mode > 4.5f)
                {
                    return EvaluateArid(angularUv, viewDir, horizonEnvelope, t, wind, speed, alphaWeight);
                }

                UNITY_BRANCH
                if (mode > 1.5f && mode < 2.5f)
                {
                    return EvaluateSnow(angularUv, horizonEnvelope, t, wind, speed, alphaWeight);
                }

                return EvaluateRain(angularUv, viewDir, azimuth, horizonEnvelope, mode, t, wind, speed, alphaWeight);
            }
            ENDCG
        }
    }
    Fallback Off
}
