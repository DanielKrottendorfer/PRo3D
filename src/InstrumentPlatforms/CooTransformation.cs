﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;

namespace JR
{
    /// <summary>
    /// Contains functions and marshalling structs to call routines from the InstrumentPlatforms.dll 
    /// provided by Joanneum Research. 
    /// </summary>
    public static partial class CooTransformation
    {
        [DllImport(@"CooTransformation.dll")]
        public static extern uint GetDllVersion();

        [DllImport(@"CooTransformation.dll")]
        public static extern int Init(string configDir, string logDir);

        [DllImport(@"CooTransformation.dll")]
        public static extern void DeInit();

        [DllImport(@"CooTransformation.dll")]
        public static extern int Xyz2LatLonRad(double dX, double dY, double dZ, ref double pdLat, ref double pdLon, ref double pdRad);

        [DllImport(@"CooTransformation.dll")]
        public static extern int Xyz2LatLonAlt(string pcPlanet, double dX, double dY, double dZ, ref double pdLat, ref double pdLon, ref double pdAlt);

        [DllImport(@"CooTransformation.dll")]
        public static extern int LatLonAlt2Xyz(string pcPlanet, double dLat, double dLon, double dAlt, ref double pdX, ref double pdY, ref double pdZ);
    }
}
