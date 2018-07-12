/*----------------------------------------------------------------------------*/
//  Project:	VIGALL
/*----------------------------------------------------------------------------*/
//  Name:		Parameters.cs
//  Purpose:	AddIn that calculates vigilance states -- Parameter class.
//  Copyright:	Copyright © University of Leipzig 2010-2016
//  Date:		2016-07-27
//  Version:	2.1
/*----------------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Text;

namespace VIGALL
{
    // IMPORTANT: Parameters are serialized automatically using the .NET XML serializer. All limitations
    // of the serializer apply: Class must be public, and only public members are serialized. 
    // IMPORTANT: Feel free to use XmlSerializer attributes to control details of serialisation. Generally,
    // adding a number of public variables to hold parameter values is sufficient for parameter
    // needs. Note that own (public) data types are supported, as are most generic collections (List<T>, ...).
    // To my knowledge, Dictionary<K,T> does NOT work, serialize the dictionary as two List<K>, List<T> 
    // collections. This is a .NET XmlSerialiwer limitation.

    /// <summary>
    /// Parameters of the Add In.
    /// </summary>
    public class Parameters
    {
        // IMPORTANT: To be serialized, parameter entries must be public.
        public string resultsPath = "C:\\Vision\\";
        public string resultsFilenameWC = "$h";
        public string resultsFilename = "results.txt";
        public bool csv = true;

        //alpha center frequency parameters
        public bool ACFauto = true; //automatically detect alpha center frequency? (ACF = alpha center finder)
        public uint ACFfrom = 0;
        public uint ACFto = 900; //900 seconds = 15 minutes
        public float alphacenter = 10;
        public bool adaptAbsThresholds = true;
        public double absThresholdA = 100000;
        public double absThresholdB23 = 200000;

        //slow eye movements parameters
        public bool SEMdetect = true;
        public string EOGname = "HEOG";
        public uint SEMlength = 6000;
        public uint SEMbefore = 6000;
        //public uint SEMafter = 6000;
        public uint SEMthreshold = 200;

        //stage C
        public uint stageClength = 30;

        //advanced tab checkboxes
        public bool batch = false;
        public bool batchexclude = false;
        public bool leaveTempNodes = false;
        public bool purgeOldTempNodes = false;

        //relative thresholds
        public double relThresholdA = 2;
        public double relThresholdA1 = 1;
        public double relThresholdA2 = 4;

        //heartbeat intervals
        public bool RRdetect = true;
        public string ECGname = "EKG";
        public uint minHeartrate = 300;
        public uint maxHeartrate = 1500;

        //skin conductance and temperature
        public bool SCLEDAdetect = true;
        public string SCLEDAname = "SCL";
        public bool TEMPdetect = true;
        public string TEMPname = "TEMP";

        //event related potentials tab
        public bool erp = false;
        public string erp_description = "ERP";
        public uint erp_before = 100;
        public uint erp_after = 1000;

        //experimental
        public bool Amygdala_LORETA = false;
        public bool adapted_bands = false;
        public bool a2pa2t = false;
        public float absThresholdsFactor = 2;
        public float lower_bound_DeltaTheta = 3;
        public double EOGfilterfrom = 0.001;
        public double EOGfilterto = 0.5;

        //deprecated
        public bool createSegments = false;
        public uint segmentLength = 1000;
        public bool new_tree = true;
        public float alpharadius = 2; //half of the width of the alpha band
    }
}