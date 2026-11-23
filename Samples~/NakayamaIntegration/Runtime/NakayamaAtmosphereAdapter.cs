// Copyright 2026 Ayase Minori and Bright Labs
// Licensed under the BSD-3-Clause License
// See LICENSE for details

using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using BrightLabs.Vendor.Atmosphere;
using BuenaVista.Core;
using BuenaVista.Diagnostics;
using BuenaVista.Utilities;

namespace Nakayama
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class NakayamaAtmosphereAdapter : UdonSharpBehaviour
    {
        [Header("References")]
        public AtmosphereController atmosphere;
        public StaffAccessManager staffAccess;

        public void RequestTimeOverride(int minutesOfDay)
        {
            if (atmosphere == null || !CanRequestOverrideLocally(StaffAccessHelper.CAP_TIME, "time")) return;

            if (IsLocalAuthority())
            {
                atmosphere.ApplyTimeOverride(minutesOfDay);
                return;
            }

            SendCustomNetworkEvent(
                NetworkEventTarget.Owner,
                nameof(OnTimeOverrideRequested),
                minutesOfDay,
                staffAccess.GetLocalCredentialHash());
        }

        public void RequestWeatherOverride(int wmoCode)
        {
            if (atmosphere == null || !CanRequestOverrideLocally(StaffAccessHelper.CAP_WEATHER, "weather")) return;

            if (IsLocalAuthority())
            {
                atmosphere.ApplyWeatherOverride(wmoCode);
                return;
            }

            SendCustomNetworkEvent(
                NetworkEventTarget.Owner,
                nameof(OnWeatherOverrideRequested),
                wmoCode,
                staffAccess.GetLocalCredentialHash());
        }

        [NetworkCallable]
        public void OnTimeOverrideRequested(int minutesOfDay, int credentialHash)
        {
            if (AuthorizeOverride(StaffAccessHelper.CAP_TIME, credentialHash, "time"))
            {
                atmosphere.ApplyTimeOverride(minutesOfDay);
            }
        }

        [NetworkCallable]
        public void OnWeatherOverrideRequested(int wmoCode, int credentialHash)
        {
            if (AuthorizeOverride(StaffAccessHelper.CAP_WEATHER, credentialHash, "weather"))
            {
                atmosphere.ApplyWeatherOverride(wmoCode);
            }
        }

        private bool AuthorizeOverride(int capability, int credentialHash, string label)
        {
            if (!Networking.IsOwner(gameObject) || atmosphere == null) return false;

            VRCPlayerApi caller = null;
            if (NetworkCalling.InNetworkCall)
            {
                caller = NetworkCalling.CallingPlayer;
                if (caller == null || !caller.IsValid()) return false;
            }

            if (staffAccess == null)
            {
                DiagnosticLog.ReportError("NakayamaAtmosphereAdapter",
                    "Staff " + label + " override refused: staffAccess unbound on owner.");
                return false;
            }

            return staffAccess.IsCallerAuthorized(caller, capability, credentialHash);
        }

        private bool IsLocalAuthority()
        {
            return Networking.LocalPlayer == null || Networking.IsOwner(gameObject);
        }

        private bool CanRequestOverrideLocally(int capability, string label)
        {
            if (staffAccess == null)
            {
                if (IsLocalAuthority()) return true;
                DiagnosticLog.ReportError("NakayamaAtmosphereAdapter", "Staff " + label + " override refused: staffAccess unbound.");
                return false;
            }

            if (!staffAccess.HasLocalCapability(capability))
            {
                DiagnosticLog.ReportWarning("NakayamaAtmosphereAdapter", "Staff " + label + " override refused: missing capability.");
                return false;
            }

            return true;
        }
    }
}
