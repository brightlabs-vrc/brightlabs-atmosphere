// Copyright 2026 Ayase Minori and Bright Labs
// Licensed under the BSD-3-Clause License
// See LICENSE for details

// Top-level enum for UdonSharp; non-special values encode their UTC offset in minutes.
namespace BrightLabs.Vendor.Atmosphere
{
    public enum AtmosphereTimeZone
    {
        HostSynced = 0,
        UTC_Minus12 = -720,
        UTC_Minus11 = -660,
        UTC_Minus10_HST = -600,
        UTC_Minus9_AKST = -540,
        UTC_Minus8_PST = -480,
        UTC_Minus7_MST = -420,
        UTC_Minus6_CST = -360,
        UTC_Minus5_EST = -300,
        UTC_Minus4_AST = -240,
        UTC_Minus3_BRT = -180,
        UTC_Minus2 = -120,
        UTC_Minus1 = -60,
        UTC = 32766,
        UTC_Plus1_CET = 60,
        UTC_Plus2_EET = 120,
        UTC_Plus3_MSK = 180,
        UTC_Plus4_GST = 240,
        UTC_Plus5_PKT = 300,
        UTC_Plus5_30_IST = 330,
        UTC_Plus6_BST = 360,
        UTC_Plus7_ICT = 420,
        UTC_Plus8_CST_SGT = 480,
        UTC_Plus9_JST_KST = 540,
        UTC_Plus9_30_ACST = 570,
        UTC_Plus10_AEST = 600,
        UTC_Plus11 = 660,
        UTC_Plus12_NZST = 720,
        UTC_Plus13 = 780,
        UTC_Plus14 = 840,
        CustomOffset = 9999
    }
}
