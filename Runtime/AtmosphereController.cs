// Copyright 2026 Ayase Minori and Bright Labs
// Licensed under the BSD-3-Clause License
// See LICENSE for details

using UdonSharp;
using UnityEngine;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;

namespace BrightLabs.Vendor.Atmosphere
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class AtmosphereController : UdonSharpBehaviour
    {
        private const int MAX_WEATHER_RETRIES = 3;
        private const float WEATHER_RETRY_DELAY_SECONDS = 10.0f;
        private const float MIN_WEATHER_POLL_INTERVAL_SECONDS = 60.0f;
        private const float ENVIRONMENT_SCALE_EPSILON = 0.01f;

        // Gates per-frame VRCShader uniform writes once transitions settle.
        private const float SHADER_GLOBAL_EPSILON = 0.0001f;

        public const int FLAG_TIME_OVERRIDE = 1;
        public const int FLAG_WEATHER_OVERRIDE = 2;

        // Precipitation props stay hidden until the blended cloud deck is thick enough.
        private const float PRECIPITATION_ACTIVATION_INTENSITY = 0.5f;

        [Header("Directional Light (Sun)")]
        [Tooltip("Singular scene Directional Light representing the sun. Falls back to a Light on this GameObject if unassigned.")]
        public Light directionalLight;

        [Tooltip("Local hour (0-24) when the sun rises above the eastern horizon.")]
        [Range(0f, 23f)]
        public float sunriseHour = 6.0f;

        [Tooltip("Local hour (0-24) when the sun sets below the western horizon.")]
        [Range(1f, 24f)]
        public float sunsetHour = 18.0f;

        [Tooltip("Peak solar elevation angle in degrees at solar noon.")]
        [Range(5f, 89.5f)]
        public float maxSunElevationDegrees = 65.0f;

        [Tooltip("Horizontal compass rotation offset in degrees for the sun's East-to-West path.")]
        [Range(-180f, 180f)]
        public float sunAzimuthOffsetDegrees = 0.0f;

        [Header("Directional Light Color & Intensity")]
        [Tooltip("When enabled, scales Directional Light color and intensity across day, golden hour, night, and weather.")]
        public bool controlLightAppearance = true;

        [Tooltip("Directional Light color at midday under clear skies.")]
        public Color dayLightColor = new Color(1.0f, 0.956f, 0.839f, 1.0f);

        [Tooltip("Directional Light color during sunrise and sunset golden hour.")]
        public Color sunsetLightColor = new Color(1.0f, 0.55f, 0.25f, 1.0f);

        [Tooltip("Directional Light color during nighttime.")]
        public Color nightLightColor = new Color(0.12f, 0.16f, 0.30f, 1.0f);

        [Tooltip("Directional Light color during full overcast or stormy daytime weather.")]
        public Color overcastLightColor = new Color(0.68f, 0.72f, 0.78f, 1.0f);

        [Tooltip("Directional Light intensity at midday under clear skies.")]
        [Min(0f)]
        public float dayLightIntensity = 1.0f;

        [Tooltip("Directional Light intensity during sunrise and sunset golden hour.")]
        [Min(0f)]
        public float sunsetLightIntensity = 0.65f;

        [Tooltip("Directional Light intensity during nighttime.")]
        [Min(0f)]
        public float nightLightIntensity = 0.05f;

        [Tooltip("Multiplier applied to daytime/sunset light intensity at maximum weather intensity (1.0).")]
        [Range(0.1f, 1.0f)]
        public float overcastLightDimFactor = 0.35f;

        [Header("Ambient & Reflection Lighting")]
        [Tooltip("Scales authored scene ambient colours and reflection intensity down at night.")]
        public bool controlEnvironmentLighting = true;

        [Tooltip("Ambient sky color at night (subtle moonlight blue).")]
        public Color nightAmbientSkyColor = new Color(0.12f, 0.18f, 0.30f, 1.0f);

        [Tooltip("Ambient equator/horizon color at night (deep slate blue).")]
        public Color nightAmbientEquatorColor = new Color(0.08f, 0.12f, 0.20f, 1.0f);

        [Tooltip("Ambient ground reflection color at night (dark cool navy tone).")]
        public Color nightAmbientGroundColor = new Color(0.04f, 0.06f, 0.10f, 1.0f);

        [Tooltip("Master multiplier applied to night ambient colors [0, 2]. 1.0 preserves authored night ambient.")]
        [Range(0f, 2f)]
        public float nightAmbientScale = 1.0f;

        [Tooltip("Fraction of the authored reflection intensity kept at deep night.")]
        [Range(0f, 1f)]
        public float nightReflectionScale = 0.2f;

        [Header("Night-Time GameObjects")]
        [Tooltip("GameObjects (floodlights, street lamps, emissive props) toggled active during the configured night window.")]
        public GameObject[] nightTimeObjects;

        [Tooltip("Local hour [0, 24) when night-time GameObjects turn ON.")]
        [Range(0f, 24f)]
        public float nightObjectsTurnOnHour = 18.0f;

        [Tooltip("Local hour [0, 24) when night-time GameObjects turn OFF.")]
        [Range(0f, 24f)]
        public float nightObjectsTurnOffHour = 6.0f;

        [Header("Timezone & Synchronization")]
        [Tooltip("HostSynced uses the instance Master's local timezone, or select a fixed UTC timezone preset.")]
        public AtmosphereTimeZone timeZone = AtmosphereTimeZone.UTC_Plus9_JST_KST;

        [Tooltip("Custom UTC offset in hours when timeZone is set to CustomOffset.")]
        [Range(-14f, 14f)]
        public float customUtcOffsetHours = 9.0f;

        [Tooltip("Synchronize clock using VRChat server Network UTC DateTime. If false, uses local system UtcNow.")]
        public bool useServerTime = true;

        [Header("Weather Visualization (Open-Meteo)")]
        [Tooltip("When enabled, the instance Host polls Open-Meteo and syncs weather state to all clients.")]
        public bool enableWeatherFetch = true;

        [Tooltip("Open-Meteo endpoint. Set per-world; default is Nakayama/Funabashi coordinates for compatibility.")]
        public VRCUrl meteoApiUrl = new VRCUrl("https://api.open-meteo.com/v1/forecast?latitude=35.7258&longitude=139.9619&current=weather_code,cloud_cover&hourly=weather_code,cloud_cover,relative_humidity_2m&forecast_hours=4");

        [Tooltip("Interval in seconds between Host weather polls (default 900s = 15 minutes). Minimum 60s.")]
        [Min(60f)]
        public float weatherRefreshIntervalSeconds = 900.0f;

        [Tooltip("Optional GameObjects toggled active when WMO weather_code indicates rain/drizzle/thunderstorm.")]
        public GameObject[] rainObjects;

        [Tooltip("Optional GameObjects toggled active when WMO weather_code indicates snowfall or snow showers.")]
        public GameObject[] snowObjects;

        [Header("Transitions")]
        [Tooltip("Seconds to sweep 12 hours of clock time. 0 snaps instantly.")]
        [Range(0f, 30f)]
        public float timeTransitionSeconds = 4.0f;

        [Tooltip("Time of day shown when the world loads, before the live clock takes over.")]
        [Range(0f, 24f)]
        public float initialPreviewTimeOfDayHours = 12.0f;

        [Tooltip("Seconds to ease a full 0 to 1 weather intensity change. 0 snaps instantly.")]
        [Range(0f, 60f)]
        public float weatherTransitionSeconds = 12.0f;

        [Header("Debug / Editor Preview")]
        [Tooltip("Override the live clock with debugTimeOfDayHours for testing and Editor scene preview.")]
        public bool useDebugTime = false;

        [Tooltip("Manual time of day in hours [0, 24) used when useDebugTime is enabled.")]
        [Range(0f, 24f)]
        public float debugTimeOfDayHours = 12.0f;

        [Tooltip("Override the synced Open-Meteo weather with debugWeatherIntensity and debugWeatherCode.")]
        public bool useDebugWeather = false;

        [Tooltip("Manual weather intensity [0, 1] used when useDebugWeather is enabled.")]
        [Range(0f, 1f)]
        public float debugWeatherIntensity = 0.0f;

        [Tooltip("Manual WMO weather code used when useDebugWeather is enabled.")]
        public int debugWeatherCode = 0;

        [UdonSynced]
        public int syncHostUtcOffsetMinutes = 540;

        [UdonSynced]
        public int syncWeatherCode = 0;

        [UdonSynced]
        public float syncWeatherIntensity = 0.0f;

        [UdonSynced]
        public bool syncHasForecast = false;

        [UdonSynced]
        public int syncForecastRevision = 0;

        [UdonSynced]
        public int[] syncForecastWeatherCodes = new int[AtmosphereHelper.FORECAST_SLOT_COUNT];

        [UdonSynced]
        public int[] syncForecastCloudCovers = new int[AtmosphereHelper.FORECAST_SLOT_COUNT];

        [UdonSynced]
        public int[] syncForecastHumidities = new int[AtmosphereHelper.FORECAST_SLOT_COUNT];

        [Header("Override State (synced)")]
        // Bitfield rather than sentinel: new synced fields deserialize as 0 so late joiners start un-overridden.
        [UdonSynced]
        public int syncAtmosphereOverrideFlags = 0;

        [UdonSynced]
        public int syncTimeOverrideMinutes = 0;

        // Kept local to the owner that applied the override; a migrated owner falls back to the next live poll.
        private bool _hasPreOverrideWeather;
        private int _preOverrideWeatherCode;
        private float _preOverrideWeatherIntensity;

        private int _udonSunDirectionId;
        private int _udonWeatherIntensityId;
        private int _udonWeatherParamsId;

        private Vector4 _lastSunDirectionGlobal;
        private float _lastWeatherIntensityGlobal;
        private Vector4 _lastWeatherParamsGlobal;

        private bool _lastNightObjectsActive;
        private bool _lastRainObjectsActive;
        private bool _lastSnowObjectsActive;

        private bool _weatherTimerScheduled;
        private int _weatherRetryCount;
        private float _currentTimeOfDayHours = 12.0f;
        private float _displayedWeatherIntensity;

        private readonly int[] _parsedForecastCodes = new int[AtmosphereHelper.FORECAST_SLOT_COUNT];
        private readonly float[] _parsedForecastClouds = new float[AtmosphereHelper.FORECAST_SLOT_COUNT];
        private readonly float[] _parsedForecastHumidities = new float[AtmosphereHelper.FORECAST_SLOT_COUNT];

        private Color _baseAmbientSkyColor;
        private Color _baseAmbientEquatorColor;
        private Color _baseAmbientGroundColor;
        private float _baseReflectionIntensity;
        private float _lastAmbientScale = 1f;
        private float _lastReflectionScale = 1f;

        private void Start()
        {
            _udonSunDirectionId = VRCShader.PropertyToID("_UdonSunDirection");
            _udonWeatherIntensityId = VRCShader.PropertyToID("_UdonWeatherIntensity");
            _udonWeatherParamsId = VRCShader.PropertyToID("_UdonWeatherParams");

            _currentTimeOfDayHours = Mathf.Repeat(initialPreviewTimeOfDayHours, 24f);

            _baseAmbientSkyColor = RenderSettings.ambientSkyColor;
            _baseAmbientEquatorColor = RenderSettings.ambientEquatorColor;
            _baseAmbientGroundColor = RenderSettings.ambientGroundColor;
            _baseReflectionIntensity = RenderSettings.reflectionIntensity;

            ResolveDirectionalLight();

            if (Networking.LocalPlayer != null)
            {
                if (Networking.IsMaster && !Networking.IsOwner(gameObject))
                {
                    Networking.SetOwner(Networking.LocalPlayer, gameObject);
                }

                if (Networking.IsOwner(gameObject))
                {
                    SyncHostTimeZoneOffset();
                    StartHostWeatherPolling();
                }
            }
            else
            {
                syncHostUtcOffsetMinutes = GetLocalSystemUtcOffsetMinutes();
                StartHostWeatherPolling();
            }

            EvaluateAndApplyCycle(true);
        }

        public override bool OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner)
        {
            return requestedOwner != null && requestedOwner.isMaster;
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (Networking.LocalPlayer != null && Networking.IsOwner(gameObject))
            {
                // Pre-override weather does not migrate across ownership transfer.
                _hasPreOverrideWeather = false;
                SyncHostTimeZoneOffset();
                StartHostWeatherPolling();
            }
        }

        public override void OnDeserialization()
        {
            EvaluateAndApplyCycle(false);
        }

        private void Update()
        {
            EvaluateAndApplyCycle(false);
        }

        public void _PollWeatherTimer()
        {
            _weatherTimerScheduled = false;
            if (!enableWeatherFetch)
            {
                return;
            }

            // Reschedule while overridden so clearing the override resumes polling on the existing cadence.
            if (IsWeatherOverrideActive())
            {
                ScheduleNextWeatherPoll();
                return;
            }

            if (IsLocalAuthority())
            {
                _weatherRetryCount = 0;
                ExecuteWeatherDownload();
                ScheduleNextWeatherPoll();
            }
        }

        public void _RetryWeatherDownload()
        {
            if (!enableWeatherFetch || IsWeatherOverrideActive())
            {
                return;
            }

            if (IsLocalAuthority())
            {
                ExecuteWeatherDownload();
            }
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            if (!IsLocalAuthority() || IsWeatherOverrideActive())
            {
                return;
            }

            if (result == null || string.IsNullOrEmpty(result.Result))
            {
                Debug.LogWarning("[BrightLabs.Atmosphere] Open-Meteo returned an empty response.");
                return;
            }

            if (!AtmosphereHelper.TryParseOpenMeteo(
                    result.Result,
                    out int parsedCode,
                    out float parsedCloudCover,
                    _parsedForecastCodes,
                    _parsedForecastClouds,
                    _parsedForecastHumidities,
                    out bool hasForecast))
            {
                Debug.LogWarning("[BrightLabs.Atmosphere] Failed to parse Open-Meteo JSON payload.");
                return;
            }

            _weatherRetryCount = 0;
            syncWeatherCode = parsedCode;
            syncWeatherIntensity = AtmosphereHelper.ComputeWeatherIntensity(parsedCode, parsedCloudCover);
            syncHasForecast = hasForecast;

            if (hasForecast)
            {
                syncForecastWeatherCodes = EnsureForecastArray(syncForecastWeatherCodes);
                syncForecastCloudCovers = EnsureForecastArray(syncForecastCloudCovers);
                syncForecastHumidities = EnsureForecastArray(syncForecastHumidities);

                for (int i = 0; i < AtmosphereHelper.FORECAST_SLOT_COUNT; i++)
                {
                    syncForecastWeatherCodes[i] = _parsedForecastCodes[i];
                    syncForecastCloudCovers[i] = Mathf.RoundToInt(_parsedForecastClouds[i]);
                    syncForecastHumidities[i] = Mathf.RoundToInt(_parsedForecastHumidities[i]);
                }

                syncForecastRevision++;
            }

            RequestSerializationIfOwner();

            Debug.Log(
                $"[BrightLabs.Atmosphere] Open-Meteo weather updated | WMO Code={syncWeatherCode} | CloudCover={parsedCloudCover:F0}% | Intensity={syncWeatherIntensity:F2} | HasForecast={syncHasForecast}");

            EvaluateAndApplyCycle(true);
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            if (!IsLocalAuthority())
            {
                return;
            }

            string err = result != null ? result.Error : "Unknown error";
            int errCode = result != null ? result.ErrorCode : -1;

            if (_weatherRetryCount < MAX_WEATHER_RETRIES)
            {
                _weatherRetryCount++;
                Debug.LogWarning(
                    $"[BrightLabs.Atmosphere] Open-Meteo fetch failed (HTTP {errCode}: {err}). Retry {_weatherRetryCount}/{MAX_WEATHER_RETRIES} in {WEATHER_RETRY_DELAY_SECONDS:F0}s. Note: 'Allow Untrusted URLs' must be enabled in VRChat settings when querying api.open-meteo.com directly.");
                SendCustomEventDelayedSeconds(nameof(_RetryWeatherDownload), WEATHER_RETRY_DELAY_SECONDS);
            }
            else
            {
                Debug.LogWarning(
                    $"[BrightLabs.Atmosphere] Open-Meteo fetch failed after {MAX_WEATHER_RETRIES} retries (HTTP {errCode}: {err}). Keeping current weather state (Code={syncWeatherCode}, Intensity={syncWeatherIntensity:F2}).");
            }
        }

        public float GetCurrentTimeOfDayHours()
        {
            return _currentTimeOfDayHours;
        }

        public float GetActiveWeatherIntensity()
        {
            return Mathf.Clamp01(useDebugWeather ? debugWeatherIntensity : syncWeatherIntensity);
        }

        public int GetActiveWeatherCode()
        {
            return useDebugWeather ? debugWeatherCode : syncWeatherCode;
        }

        public int GetForecastRevision()
        {
            return syncForecastRevision;
        }

        public bool TryGetForecastSlot(
            int slotIndex,
            out int weatherCode,
            out float cloudCoverPercent,
            out float relativeHumidityPercent)
        {
            weatherCode = 0;
            cloudCoverPercent = 0f;
            relativeHumidityPercent = 50f;

            if (!syncHasForecast || useDebugWeather || IsWeatherOverrideActive())
            {
                return false;
            }

            if (slotIndex < 0 || slotIndex >= AtmosphereHelper.FORECAST_SLOT_COUNT)
            {
                return false;
            }

            if (syncForecastWeatherCodes == null || slotIndex >= syncForecastWeatherCodes.Length
                || syncForecastCloudCovers == null || slotIndex >= syncForecastCloudCovers.Length
                || syncForecastHumidities == null || slotIndex >= syncForecastHumidities.Length)
            {
                return false;
            }

            weatherCode = syncForecastWeatherCodes[slotIndex];
            cloudCoverPercent = syncForecastCloudCovers[slotIndex];
            relativeHumidityPercent = syncForecastHumidities[slotIndex];
            return true;
        }

        public bool IsNightObjectsActive()
        {
            return _lastNightObjectsActive;
        }

        public void SetTimeZone(AtmosphereTimeZone newTimeZone)
        {
            timeZone = newTimeZone;
            if (timeZone == AtmosphereTimeZone.HostSynced
                && Networking.LocalPlayer != null
                && Networking.IsOwner(gameObject))
            {
                SyncHostTimeZoneOffset();
            }

            EvaluateAndApplyCycle(true);
        }

        public bool IsTimeOverrideActive()
        {
            return (syncAtmosphereOverrideFlags & FLAG_TIME_OVERRIDE) != 0;
        }

        public bool IsWeatherOverrideActive()
        {
            return (syncAtmosphereOverrideFlags & FLAG_WEATHER_OVERRIDE) != 0;
        }

        public int GetTimeOverrideMinutes()
        {
            return syncTimeOverrideMinutes;
        }

        public void ApplyTimeOverride(int minutesOfDay)
        {
            bool clear = minutesOfDay < 0;
            if (clear)
            {
                syncAtmosphereOverrideFlags &= ~FLAG_TIME_OVERRIDE;
            }
            else
            {
                syncTimeOverrideMinutes = AtmosphereHelper.NormalizeMinutesOfDay(minutesOfDay);
                syncAtmosphereOverrideFlags |= FLAG_TIME_OVERRIDE;
            }

            RequestSerializationIfOwner();
            EvaluateAndApplyCycle(true);

            Debug.Log(clear
                ? "[BrightLabs.Atmosphere] Time override cleared; following the live clock."
                : "[BrightLabs.Atmosphere] Time override applied | minutes=" + syncTimeOverrideMinutes + " | flags=" + syncAtmosphereOverrideFlags);
        }

        public void ApplyWeatherOverride(int wmoCode)
        {
            bool clear = wmoCode < 0;
            if (!clear && !AtmosphereHelper.IsValidWeatherCode(wmoCode))
            {
                Debug.LogWarning("[BrightLabs.Atmosphere] Weather override ignored: WMO code " + wmoCode + " is not distinguished by the atmosphere.");
                return;
            }

            if (clear)
            {
                if (_hasPreOverrideWeather)
                {
                    syncWeatherCode = _preOverrideWeatherCode;
                    syncWeatherIntensity = _preOverrideWeatherIntensity;
                    _hasPreOverrideWeather = false;
                }
                syncAtmosphereOverrideFlags &= ~FLAG_WEATHER_OVERRIDE;
            }
            else
            {
                if (!_hasPreOverrideWeather)
                {
                    _preOverrideWeatherCode = syncWeatherCode;
                    _preOverrideWeatherIntensity = syncWeatherIntensity;
                    _hasPreOverrideWeather = true;
                }
                syncWeatherCode = wmoCode;
                syncWeatherIntensity = AtmosphereHelper.ComputeWeatherIntensity(wmoCode, -1f);
                syncAtmosphereOverrideFlags |= FLAG_WEATHER_OVERRIDE;
            }

            RequestSerializationIfOwner();
            EvaluateAndApplyCycle(true);

            if (clear && enableWeatherFetch)
            {
                _weatherRetryCount = 0;
                ExecuteWeatherDownload();
                ScheduleNextWeatherPoll();
            }

            Debug.Log(clear
                ? "[BrightLabs.Atmosphere] Weather override cleared; following live Open-Meteo weather."
                : "[BrightLabs.Atmosphere] Weather override applied | code=" + syncWeatherCode + " | intensity=" + syncWeatherIntensity + " | flags=" + syncAtmosphereOverrideFlags);
        }

        private bool IsLocalAuthority()
        {
            return Networking.LocalPlayer == null || Networking.IsOwner(gameObject);
        }

        private void RequestSerializationIfOwner()
        {
            if (Networking.LocalPlayer != null && Networking.IsOwner(gameObject))
            {
                RequestSerialization();
            }
        }

        private int[] EnsureForecastArray(int[] array)
        {
            return array != null && array.Length == AtmosphereHelper.FORECAST_SLOT_COUNT
                ? array
                : new int[AtmosphereHelper.FORECAST_SLOT_COUNT];
        }

        private void ResolveDirectionalLight()
        {
            if (directionalLight == null)
            {
                directionalLight = GetComponent<Light>();
            }

            if (directionalLight == null)
            {
                Debug.LogWarning(
                    "[BrightLabs.Atmosphere] directionalLight is not assigned and no Light component exists on this GameObject. Sun transform and light properties will not be updated.",
                    this);
            }
        }

        private void SyncHostTimeZoneOffset()
        {
            syncHostUtcOffsetMinutes = GetLocalSystemUtcOffsetMinutes();
            RequestSerialization();
            Debug.Log($"[BrightLabs.Atmosphere] Host synced local UTC offset: {syncHostUtcOffsetMinutes} minutes.");
        }

        private void StartHostWeatherPolling()
        {
            if (!enableWeatherFetch || IsWeatherOverrideActive())
            {
                return;
            }

            _weatherRetryCount = 0;
            ExecuteWeatherDownload();
            ScheduleNextWeatherPoll();
        }

        private void ExecuteWeatherDownload()
        {
            if (meteoApiUrl == null || string.IsNullOrEmpty(meteoApiUrl.Get()))
            {
                Debug.LogWarning("[BrightLabs.Atmosphere] meteoApiUrl is unassigned or empty; skipping Open-Meteo fetch.");
                return;
            }

            VRCStringDownloader.LoadUrl(meteoApiUrl, this);
        }

        private void ScheduleNextWeatherPoll()
        {
            if (_weatherTimerScheduled || !enableWeatherFetch)
            {
                return;
            }

            float interval = Mathf.Max(MIN_WEATHER_POLL_INTERVAL_SECONDS, weatherRefreshIntervalSeconds);
            _weatherTimerScheduled = true;
            SendCustomEventDelayedSeconds(nameof(_PollWeatherTimer), interval);
        }

        private int GetLocalSystemUtcOffsetMinutes()
        {
            System.TimeSpan offset = System.DateTime.Now - System.DateTime.UtcNow;
            int roundedMinutes = Mathf.RoundToInt((float)offset.TotalMinutes);
            return Mathf.Clamp(
                roundedMinutes,
                AtmosphereHelper.MIN_UTC_OFFSET_MINUTES,
                AtmosphereHelper.MAX_UTC_OFFSET_MINUTES);
        }

        private void EvaluateAndApplyCycle(bool forceRefresh)
        {
            float targetTimeOfDayHours = ComputeTargetTimeOfDayHours();
            _currentTimeOfDayHours = timeTransitionSeconds > 0.001f
                ? AtmosphereHelper.BlendTimeOfDayHours(
                    _currentTimeOfDayHours,
                    targetTimeOfDayHours,
                    (12f / timeTransitionSeconds) * Time.deltaTime)
                : Mathf.Repeat(targetTimeOfDayHours, 24f);

            float targetWeatherIntensity = GetActiveWeatherIntensity();
            int activeWeatherCode = GetActiveWeatherCode();
            _displayedWeatherIntensity = weatherTransitionSeconds > 0.001f
                ? Mathf.MoveTowards(_displayedWeatherIntensity, targetWeatherIntensity, Time.deltaTime / weatherTransitionSeconds)
                : targetWeatherIntensity;

            AtmosphereHelper.ComputeSunTrajectory(
                _currentTimeOfDayHours,
                sunriseHour,
                sunsetHour,
                maxSunElevationDegrees,
                sunAzimuthOffsetDegrees,
                out Vector3 sunDirection,
                out Quaternion lightRotation);

            ApplyDirectionalLight(lightRotation, sunDirection.y, _displayedWeatherIntensity);
            ApplyEnvironmentLighting(sunDirection.y, forceRefresh);
            ApplyShaderGlobals(sunDirection, activeWeatherCode, _displayedWeatherIntensity, forceRefresh);
            ApplyAtmosphereObjects(activeWeatherCode, forceRefresh);
        }

        private float ComputeTargetTimeOfDayHours()
        {
            if (useDebugTime)
            {
                return Mathf.Repeat(debugTimeOfDayHours, 24f);
            }

            if (IsTimeOverrideActive())
            {
                return AtmosphereHelper.MinutesToHours(
                    AtmosphereHelper.NormalizeMinutesOfDay(syncTimeOverrideMinutes));
            }

            System.DateTime utcNow = GetCurrentNetworkDateTime();
            int offsetMinutes = AtmosphereHelper.ResolveUtcOffsetMinutes(
                timeZone,
                syncHostUtcOffsetMinutes,
                customUtcOffsetHours);

            return AtmosphereHelper.ComputeTimeOfDayHours(
                utcNow.Hour,
                utcNow.Minute,
                utcNow.Second,
                utcNow.Millisecond,
                offsetMinutes);
        }

        private void ApplyDirectionalLight(Quaternion lightRotation, float sunDirY, float weatherIntensity)
        {
            if (directionalLight == null)
            {
                return;
            }

            directionalLight.transform.rotation = lightRotation;

            if (!controlLightAppearance)
            {
                return;
            }

            AtmosphereHelper.ComputeLightAppearance(
                sunDirY,
                weatherIntensity,
                dayLightColor,
                sunsetLightColor,
                nightLightColor,
                overcastLightColor,
                dayLightIntensity,
                sunsetLightIntensity,
                nightLightIntensity,
                overcastLightDimFactor,
                out Color targetColor,
                out float targetIntensity);

            directionalLight.color = targetColor;
            directionalLight.intensity = targetIntensity;
        }

        private void ApplyShaderGlobals(Vector3 sunDirection, int weatherCode, float weatherIntensity, bool forceApply)
        {
            AtmosphereHelper.ComputeWeatherShaderParams(weatherCode, weatherIntensity, out Vector4 weatherShaderParams);
            Vector4 sunDirectionGlobal = new Vector4(sunDirection.x, sunDirection.y, sunDirection.z, 0f);

            if (!forceApply && !ShaderGlobalsChanged(sunDirectionGlobal, weatherIntensity, weatherShaderParams))
            {
                return;
            }

            _lastSunDirectionGlobal = sunDirectionGlobal;
            _lastWeatherIntensityGlobal = weatherIntensity;
            _lastWeatherParamsGlobal = weatherShaderParams;

            VRCShader.SetGlobalVector(_udonSunDirectionId, sunDirectionGlobal);
            VRCShader.SetGlobalFloat(_udonWeatherIntensityId, weatherIntensity);
            VRCShader.SetGlobalVector(_udonWeatherParamsId, weatherShaderParams);
        }

        private bool ShaderGlobalsChanged(Vector4 sunDirection, float weatherIntensity, Vector4 weatherParams)
        {
            return Mathf.Abs(weatherIntensity - _lastWeatherIntensityGlobal) > SHADER_GLOBAL_EPSILON
                || Vector4.Distance(sunDirection, _lastSunDirectionGlobal) > SHADER_GLOBAL_EPSILON
                || Vector4.Distance(weatherParams, _lastWeatherParamsGlobal) > SHADER_GLOBAL_EPSILON;
        }

        private void ApplyAtmosphereObjects(int activeWeatherCode, bool forceRefresh)
        {
            bool shouldNightObjectsBeActive = AtmosphereHelper.IsWithinHourWindow(
                _currentTimeOfDayHours,
                nightObjectsTurnOnHour,
                nightObjectsTurnOffHour);

            if (forceRefresh || shouldNightObjectsBeActive != _lastNightObjectsActive)
            {
                _lastNightObjectsActive = shouldNightObjectsBeActive;
                SetObjectArrayActive(nightTimeObjects, shouldNightObjectsBeActive);
            }

            AtmosphereHelper.EvaluatePrecipitationFlags(
                activeWeatherCode,
                out bool isRainWeather,
                out bool isSnowWeather);

            // Props follow the blended intensity so clearing weather fades them out with the cloud deck.
            bool precipitationVisible = _displayedWeatherIntensity >= PRECIPITATION_ACTIVATION_INTENSITY;
            bool shouldRainBeActive = isRainWeather && precipitationVisible;
            bool shouldSnowBeActive = isSnowWeather && precipitationVisible;

            if (forceRefresh
                || shouldRainBeActive != _lastRainObjectsActive
                || shouldSnowBeActive != _lastSnowObjectsActive)
            {
                _lastRainObjectsActive = shouldRainBeActive;
                _lastSnowObjectsActive = shouldSnowBeActive;
                SetObjectArrayActive(rainObjects, shouldRainBeActive);
                SetObjectArrayActive(snowObjects, shouldSnowBeActive);
            }
        }

        private void ApplyEnvironmentLighting(float sunDirY, bool forceApply)
        {
            if (!controlEnvironmentLighting)
            {
                return;
            }

            float nightBlend = AtmosphereHelper.ComputeNightBlend(sunDirY);
            float reflectionScale = Mathf.Lerp(1f, Mathf.Clamp01(nightReflectionScale), nightBlend);

            if (forceApply || Mathf.Abs(nightBlend - _lastAmbientScale) > ENVIRONMENT_SCALE_EPSILON)
            {
                _lastAmbientScale = nightBlend;

                Color targetSky = AtmosphereHelper.ScaleAmbientColor(nightAmbientSkyColor, nightAmbientScale);
                Color targetEquator = AtmosphereHelper.ScaleAmbientColor(nightAmbientEquatorColor, nightAmbientScale);
                Color targetGround = AtmosphereHelper.ScaleAmbientColor(nightAmbientGroundColor, nightAmbientScale);

                RenderSettings.ambientSkyColor = Color.Lerp(_baseAmbientSkyColor, targetSky, nightBlend);
                RenderSettings.ambientEquatorColor = Color.Lerp(_baseAmbientEquatorColor, targetEquator, nightBlend);
                RenderSettings.ambientGroundColor = Color.Lerp(_baseAmbientGroundColor, targetGround, nightBlend);
            }

            if (forceApply || Mathf.Abs(reflectionScale - _lastReflectionScale) > ENVIRONMENT_SCALE_EPSILON)
            {
                _lastReflectionScale = reflectionScale;
                RenderSettings.reflectionIntensity = _baseReflectionIntensity * reflectionScale;
            }
        }

        private void SetObjectArrayActive(GameObject[] objects, bool isActive)
        {
            if (objects == null)
            {
                return;
            }

            for (int i = 0; i < objects.Length; i++)
            {
                GameObject obj = objects[i];
                if (obj != null && obj.activeSelf != isActive)
                {
                    obj.SetActive(isActive);
                }
            }
        }

        private System.DateTime GetCurrentNetworkDateTime()
        {
            if (useServerTime && Networking.LocalPlayer != null)
            {
                System.DateTime netTime = Networking.GetNetworkDateTime();
                if (netTime.Year >= 2020)
                {
                    return netTime;
                }
            }

            return System.DateTime.UtcNow;
        }

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        private void OnValidate()
        {
            if (Application.isPlaying || (!useDebugTime && !useDebugWeather))
            {
                return;
            }

            float previewHour = useDebugTime ? Mathf.Repeat(debugTimeOfDayHours, 24f) : 12.0f;
            float previewWeatherIntensity = useDebugWeather ? Mathf.Clamp01(debugWeatherIntensity) : 0.0f;
            int previewWeatherCode = useDebugWeather ? debugWeatherCode : 0;

            AtmosphereHelper.ComputeSunTrajectory(
                previewHour,
                sunriseHour,
                sunsetHour,
                maxSunElevationDegrees,
                sunAzimuthOffsetDegrees,
                out Vector3 sunDirection,
                out Quaternion lightRotation);

            ApplyDirectionalLight(lightRotation, sunDirection.y, previewWeatherIntensity);

            Shader.SetGlobalVector("_UdonSunDirection", new Vector4(sunDirection.x, sunDirection.y, sunDirection.z, 0f));
            Shader.SetGlobalFloat("_UdonWeatherIntensity", previewWeatherIntensity);
            AtmosphereHelper.ComputeWeatherShaderParams(
                previewWeatherCode,
                previewWeatherIntensity,
                out Vector4 previewWeatherParams);
            Shader.SetGlobalVector("_UdonWeatherParams", previewWeatherParams);

            SetObjectArrayActive(
                nightTimeObjects,
                AtmosphereHelper.IsWithinHourWindow(previewHour, nightObjectsTurnOnHour, nightObjectsTurnOffHour));

            AtmosphereHelper.EvaluatePrecipitationFlags(
                previewWeatherCode,
                out bool shouldRainBeActive,
                out bool shouldSnowBeActive);
            SetObjectArrayActive(rainObjects, shouldRainBeActive);
            SetObjectArrayActive(snowObjects, shouldSnowBeActive);
        }
#endif
    }
}
