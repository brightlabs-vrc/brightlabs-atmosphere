// Copyright 2026 Ayase Minori and Bright Labs
// Licensed under the BSD-3-Clause License
// See LICENSE for details

using UnityEngine;
using VRC.SDK3.Data;

namespace BrightLabs.Vendor.Atmosphere
{
    public static class AtmosphereHelper
    {
        public const int MIN_UTC_OFFSET_MINUTES = -840;
        public const int MAX_UTC_OFFSET_MINUTES = 840;
        public const int FORECAST_SLOT_COUNT = 3;
        public const int MINUTES_PER_DAY = 1440;

        private const float HOURS_TO_DEGREES = 15f;

        public static int ResolveUtcOffsetMinutes(
            AtmosphereTimeZone timeZone,
            int syncedHostOffsetMinutes,
            float customOffsetHours)
        {
            int minutes;
            switch (timeZone)
            {
                case AtmosphereTimeZone.HostSynced:
                    minutes = syncedHostOffsetMinutes;
                    break;
                case AtmosphereTimeZone.CustomOffset:
                    minutes = Mathf.RoundToInt(customOffsetHours * 60f);
                    break;
                case AtmosphereTimeZone.UTC:
                    minutes = 0;
                    break;
                default:
                    minutes = (int)timeZone;
                    break;
            }

            return Mathf.Clamp(minutes, MIN_UTC_OFFSET_MINUTES, MAX_UTC_OFFSET_MINUTES);
        }

        public static float ComputeTimeOfDayHours(
            int utcHour,
            int utcMinute,
            int utcSecond,
            int utcMillisecond,
            int utcOffsetMinutes)
        {
            float totalSeconds = (utcHour * 3600f)
                               + (utcMinute * 60f)
                               + utcSecond
                               + (utcMillisecond * 0.001f)
                               + (utcOffsetMinutes * 60f);

            return Mathf.Repeat(totalSeconds / 3600f, 24f);
        }

        public static float BlendTimeOfDayHours(float from, float to, float maxStepHours)
        {
            float nextAngle = Mathf.MoveTowardsAngle(
                from * HOURS_TO_DEGREES,
                to * HOURS_TO_DEGREES,
                Mathf.Max(0f, maxStepHours) * HOURS_TO_DEGREES);
            return Mathf.Repeat(nextAngle / HOURS_TO_DEGREES, 24f);
        }

        public static bool IsWithinHourWindow(float currentHour, float turnOnHour, float turnOffHour)
        {
            float wrappedCurrent = Mathf.Repeat(currentHour, 24f);
            float clampedOn = Mathf.Clamp(turnOnHour, 0f, 24f);
            float clampedOff = Mathf.Clamp(turnOffHour, 0f, 24f);

            if (Mathf.Approximately(clampedOn, clampedOff))
            {
                return false;
            }

            return clampedOn < clampedOff
                ? wrappedCurrent >= clampedOn && wrappedCurrent < clampedOff
                : wrappedCurrent >= clampedOn || wrappedCurrent < clampedOff;
        }

        public static void ComputeSunTrajectory(
            float timeOfDayHours,
            float sunriseHour,
            float sunsetHour,
            float maxElevationDegrees,
            float azimuthOffsetDegrees,
            out Vector3 sunDirection,
            out Quaternion lightRotation)
        {
            float wrappedHour = Mathf.Repeat(timeOfDayHours, 24f);
            float safeSunrise = Mathf.Clamp(sunriseHour, 0f, 23.0f);
            float safeSunset = Mathf.Clamp(sunsetHour, safeSunrise + 0.5f, 24.0f);
            float peakElevation = Mathf.Clamp(maxElevationDegrees, 5f, 89.5f);

            float elevationDeg;
            float azimuthDeg;

            if (wrappedHour >= safeSunrise && wrappedHour <= safeSunset)
            {
                float dayT = Mathf.InverseLerp(safeSunrise, safeSunset, wrappedHour);
                elevationDeg = Mathf.Sin(dayT * Mathf.PI) * peakElevation;
                azimuthDeg = Mathf.Lerp(90f, 270f, dayT) + azimuthOffsetDegrees;
            }
            else
            {
                float nightDuration = 24f - (safeSunset - safeSunrise);
                float hoursSinceSunset = Mathf.Repeat(wrappedHour - safeSunset, 24f);
                float nightT = Mathf.InverseLerp(0f, nightDuration, hoursSinceSunset);
                elevationDeg = -Mathf.Sin(nightT * Mathf.PI) * peakElevation;
                azimuthDeg = Mathf.Lerp(270f, 450f, nightT) + azimuthOffsetDegrees;
            }

            float elevRad = elevationDeg * Mathf.Deg2Rad;
            float azimRad = azimuthDeg * Mathf.Deg2Rad;
            float cosElev = Mathf.Cos(elevRad);

            sunDirection = new Vector3(
                cosElev * Mathf.Sin(azimRad),
                Mathf.Sin(elevRad),
                cosElev * Mathf.Cos(azimRad)).normalized;

            // At night the directional light acts as the moon opposite the sun and shines downward.
            Vector3 lightForward = sunDirection.y >= 0f ? -sunDirection : sunDirection;
            lightRotation = Quaternion.LookRotation(lightForward, Vector3.up);
        }

        // Shared threshold so directional light and RenderSettings ambient/reflection dim together.
        public static float ComputeNightBlend(float sunDirY)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, -0.15f, sunDirY));
        }

        public static Color ScaleAmbientColor(Color source, float scale)
        {
            return new Color(source.r * scale, source.g * scale, source.b * scale, source.a);
        }

        public static void ComputeLightAppearance(
            float sunDirY,
            float weatherIntensity,
            Color dayColor,
            Color sunsetColor,
            Color nightColor,
            Color overcastDayColor,
            float dayIntensity,
            float sunsetIntensity,
            float nightIntensity,
            float overcastLightDimFactor,
            out Color finalColor,
            out float finalIntensity)
        {
            float clampedWeather = Mathf.Clamp01(weatherIntensity);
            float nightT = ComputeNightBlend(sunDirY);
            float horizonProximity = Mathf.InverseLerp(0.35f, 0f, Mathf.Abs(sunDirY));
            float aboveDeepNight = Mathf.InverseLerp(-0.2f, 0f, sunDirY);
            float goldenT = Mathf.SmoothStep(0f, 1f, horizonProximity) * aboveDeepNight;

            Color clearDayColor = Color.Lerp(dayColor, sunsetColor, goldenT);
            Color weatherDayColor = Color.Lerp(clearDayColor, overcastDayColor, clampedWeather);
            finalColor = Color.Lerp(weatherDayColor, nightColor, nightT);

            float clearDayIntensity = Mathf.Lerp(dayIntensity, sunsetIntensity, goldenT);
            float weatherDimMultiplier = Mathf.Lerp(1.0f, Mathf.Clamp(overcastLightDimFactor, 0.05f, 1.0f), clampedWeather);
            finalIntensity = Mathf.Lerp(clearDayIntensity * weatherDimMultiplier, nightIntensity, nightT);
        }

        public static bool TryParseOpenMeteo(
            string json,
            out int weatherCode,
            out float cloudCoverPercent,
            int[] outForecastCodes,
            float[] outForecastClouds,
            float[] outForecastHumidities,
            out bool hasForecast)
        {
            weatherCode = 0;
            cloudCoverPercent = -1f;
            hasForecast = false;

            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            if (!VRCJson.TryDeserializeFromJson(json, out DataToken rootToken)
                || rootToken.TokenType != TokenType.DataDictionary)
            {
                return false;
            }

            DataDictionary rootDict = rootToken.DataDictionary;
            if (!TryParseCurrentWeather(rootDict, out weatherCode, out cloudCoverPercent))
            {
                return false;
            }

            hasForecast = TryParseHourlyForecast(
                rootDict, outForecastCodes, outForecastClouds, outForecastHumidities);
            return true;
        }

        private static bool TryParseCurrentWeather(
            DataDictionary rootDict,
            out int weatherCode,
            out float cloudCoverPercent)
        {
            weatherCode = 0;
            cloudCoverPercent = -1f;

            if (rootDict.TryGetValue("current", TokenType.DataDictionary, out DataToken currentToken))
            {
                DataDictionary currentDict = currentToken.DataDictionary;
                if (currentDict.TryGetValue("weather_code", out DataToken codeToken) && codeToken.IsNumber)
                {
                    weatherCode = Mathf.RoundToInt((float)codeToken.Double);
                    if (currentDict.TryGetValue("cloud_cover", out DataToken cloudToken) && cloudToken.IsNumber)
                    {
                        cloudCoverPercent = Mathf.Clamp((float)cloudToken.Double, 0f, 100f);
                    }
                    return true;
                }
            }

            if (rootDict.TryGetValue("current_weather", TokenType.DataDictionary, out DataToken legacyToken))
            {
                DataDictionary legacyDict = legacyToken.DataDictionary;
                if (legacyDict.TryGetValue("weathercode", out DataToken legacyCodeToken) && legacyCodeToken.IsNumber)
                {
                    weatherCode = Mathf.RoundToInt((float)legacyCodeToken.Double);
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseHourlyForecast(
            DataDictionary rootDict,
            int[] outWeatherCodes,
            float[] outCloudCovers,
            float[] outHumidities)
        {
            if (!rootDict.TryGetValue("hourly", TokenType.DataDictionary, out DataToken hourlyToken))
            {
                return false;
            }

            DataDictionary hourlyDict = hourlyToken.DataDictionary;
            if (!hourlyDict.TryGetValue("weather_code", TokenType.DataList, out DataToken codesToken))
            {
                return false;
            }

            DataList codesList = codesToken.DataList;
            // Open-Meteo forecast_hours=4 places the current hour at index 0, so slots 0..2 read indices 1..3.
            if (codesList.Count < FORECAST_SLOT_COUNT + 1)
            {
                return false;
            }

            DataList cloudsList = hourlyDict.TryGetValue("cloud_cover", TokenType.DataList, out DataToken cloudsToken)
                ? cloudsToken.DataList
                : null;
            DataList humiditiesList = hourlyDict.TryGetValue("relative_humidity_2m", TokenType.DataList, out DataToken humiditiesToken)
                ? humiditiesToken.DataList
                : null;

            for (int slot = 0; slot < FORECAST_SLOT_COUNT; slot++)
            {
                int hourIndex = slot + 1;
                if (!codesList.TryGetValue(hourIndex, out DataToken codeToken) || !codeToken.IsNumber)
                {
                    return false;
                }

                outWeatherCodes[slot] = Mathf.RoundToInt((float)codeToken.Double);
                outCloudCovers[slot] = ReadClampedPercent(cloudsList, hourIndex, 0f);
                outHumidities[slot] = ReadClampedPercent(humiditiesList, hourIndex, 50f);
            }

            return true;
        }

        private static float ReadClampedPercent(DataList list, int index, float fallback)
        {
            if (list != null && index < list.Count
                && list.TryGetValue(index, out DataToken token) && token.IsNumber)
            {
                return Mathf.Clamp((float)token.Double, 0f, 100f);
            }

            return fallback;
        }

        public static float ComputeWeatherIntensity(int wmoWeatherCode, float cloudCoverPercent)
        {
            float codeIntensity;

            if (wmoWeatherCode <= 0 || wmoWeatherCode == 100)
            {
                codeIntensity = 0.00f;
            }
            else if (wmoWeatherCode >= 95 && wmoWeatherCode <= 99)
            {
                codeIntensity = 1.00f;
            }
            else
            {
                switch (wmoWeatherCode)
                {
                    case 1:
                        codeIntensity = 0.20f;
                        break;
                    case 2:
                        codeIntensity = 0.45f;
                        break;
                    case 51:
                    case 56:
                        codeIntensity = 0.65f;
                        break;
                    case 3:
                    case 53:
                    case 61:
                    case 66:
                    case 71:
                    case 77:
                    case 80:
                    case 85:
                        codeIntensity = 0.75f;
                        break;
                    case 45:
                    case 48:
                        codeIntensity = 0.80f;
                        break;
                    case 55:
                    case 57:
                        codeIntensity = 0.85f;
                        break;
                    case 63:
                    case 73:
                    case 81:
                        codeIntensity = 0.88f;
                        break;
                    case 65:
                    case 67:
                    case 75:
                    case 82:
                    case 86:
                        codeIntensity = 1.00f;
                        break;
                    default:
                        codeIntensity = 0.50f;
                        break;
                }
            }

            if (cloudCoverPercent >= 0f)
            {
                float cloudContribution = Mathf.Clamp01(cloudCoverPercent * 0.01f) * 0.75f;
                return Mathf.Max(codeIntensity, cloudContribution);
            }

            return codeIntensity;
        }

        public static void EvaluatePrecipitationFlags(
            int wmoWeatherCode,
            out bool isRaining,
            out bool isSnowing)
        {
            isRaining = IsRainCode(wmoWeatherCode) || (wmoWeatherCode >= 95 && wmoWeatherCode <= 99);
            isSnowing = IsSnowCode(wmoWeatherCode);
        }

        // Packs _UdonWeatherParams for Nakayama/WeatherOverlay: x=mode, y=opacity, z=wind slant, w=speed.
        public static void ComputeWeatherShaderParams(
            int wmoWeatherCode,
            float displayedWeatherIntensity,
            out Vector4 weatherParams)
        {
            if (wmoWeatherCode == 100)
            {
                float aridAlpha = Mathf.InverseLerp(0.5f, 0f, displayedWeatherIntensity) * 0.65f;
                weatherParams = new Vector4(5f, aridAlpha, 0.22f, 0.18f);
                return;
            }

            // Clouds thicken above 0.40 before precipitation fades in.
            float precipVisibility = Mathf.InverseLerp(0.40f, 1.0f, displayedWeatherIntensity);
            if (precipVisibility <= 0.001f)
            {
                weatherParams = Vector4.zero;
                return;
            }

            if (wmoWeatherCode >= 96 && wmoWeatherCode <= 99)
            {
                weatherParams = new Vector4(4f, precipVisibility, 0.68f, 2.25f);
                return;
            }

            if (wmoWeatherCode == 95)
            {
                weatherParams = new Vector4(3f, precipVisibility * 0.90f, 0.36f, 1.60f);
                return;
            }

            if (IsSnowCode(wmoWeatherCode))
            {
                weatherParams = new Vector4(2f, precipVisibility * 0.85f, 0.15f, 0.30f);
                return;
            }

            if (IsRainCode(wmoWeatherCode))
            {
                weatherParams = new Vector4(1f, precipVisibility * 0.75f, 0.09f, 1.05f);
                return;
            }

            weatherParams = Vector4.zero;
        }

        public static int NormalizeMinutesOfDay(int minutes)
        {
            int wrapped = minutes % MINUTES_PER_DAY;
            return wrapped < 0 ? wrapped + MINUTES_PER_DAY : wrapped;
        }

        public static float MinutesToHours(int minutes)
        {
            return NormalizeMinutesOfDay(minutes) / 60.0f;
        }

        public static int HoursToMinutes(float hours)
        {
            return NormalizeMinutesOfDay(Mathf.RoundToInt(hours * 60.0f));
        }

        public static bool IsValidWeatherCode(int wmoCode)
        {
            switch (wmoCode)
            {
                case 0:
                case 1:
                case 2:
                case 3:
                case 45:
                case 48:
                case 51:
                case 53:
                case 55:
                case 56:
                case 57:
                case 61:
                case 63:
                case 65:
                case 66:
                case 67:
                case 71:
                case 73:
                case 75:
                case 77:
                case 80:
                case 81:
                case 82:
                case 85:
                case 86:
                case 95:
                case 96:
                case 99:
                case 100:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsRainCode(int code)
        {
            return (code >= 51 && code <= 67) || (code >= 80 && code <= 82);
        }

        private static bool IsSnowCode(int code)
        {
            return (code >= 71 && code <= 77) || code == 85 || code == 86;
        }
    }
}
