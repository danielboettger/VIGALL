/*----------------------------------------------------------------------------*/
//  Project:	VIGALL
/*----------------------------------------------------------------------------*/
//  Name:		AddIn.cs
//  Purpose:	AddIn that calculates VIGALL vigilance states.
//  Copyright:	Copyright © University of Leipzig 2011-2018
//  Date:		2018-07-12
//  Version:	2.1
/*----------------------------------------------------------------------------*/

/* Note to future developers of this codebase: please excuse idiosyncrasies and some convoluted structures in the code.
   I developed this code alone and hence was not forced to adhere to common coding style standards.
   This software was created very organically, over hundreds of builds to satisfy iterative change requests coming in from
   the researchers developing the algorithm and testing it. In many cases, some small change or addition had to be made
   without breaking anything else. I have attempted to document everything as clearly as possible, but it remains very
   much unlike properly designed code.*/

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using BrainVision.Interfaces;
using BrainVision.Support;
using BrainVision.AnalyzerAutomation;

namespace VIGALL
{
    /// <summary>
    /// Main Class of module, implements IAnalyzerAddIn interface.
    /// </summary>
    [AddIn(VigallId, "VIGALL", "VIGALL 2.1 Vigilance Algorithm Leipzig", 0, 1000000)]
    public class AddIn : IAnalyzerAddIn
    {
        #region AddIn info
        const string VigallId = "{F02D80A6-5777-4439-8439-39E99C3D4453}";


        
        /// <summary>
        /// Provides info about this module. Used to generate menu entries etc. in main program.
        /// </summary>
        /// <param name="sInfo">Info topic</param>
        /// <param name="other">Optional additional argument</param>
        /// <returns>Information value</returns>
        public object GetInfo(string sInfo, object other)
        {
            switch (sInfo)
            {
                case ComponentInfos.MenuText:
                    return "VIGALL";
                case ComponentInfos.HelpText:
                    return "Calculates vigilance states.";
                case ComponentInfos.AutomationName:
                    return "none";
                case ComponentInfos.WindowTitle:
                    return "VIGALL v.2.1";
                case ComponentInfos.Visible:
                    return true;
                default:
                    return "";
            }
        }

        #endregion //AddIn info

        public void Execute() //main method
        {
            #region Find Active Node
            /* here we look at the node to work on, and check whether it has the right properties for VIGALL*/

            _analyzer = AutomationSupport.Application;//basic setup
            if (_analyzer.ActiveNode == null)
            {
                MessageDisplay.ShowMessage("VIGALL::Execute", Versionstring, "Please select a node in a history file.");
                return;
            }
            _hf = _analyzer.ActiveNode.HistoryFile; //main data structure, (almost) everything else derives from here

            if (_analyzer.ActiveNode.Dataset == null) //this should only happen if Analyzer is in Marker Edit mode
            {
                MessageDisplay.ShowMessage("VIGALL::Execute", Versionstring, "Cannot run VIGALL from Marker Edit mode.");
                return;
            }
            //points = activeNode.Dataset.Length; //and that's all we need from the main dataset for now

            if(_analyzer.TemplateMode)
            {
                MessageDisplay.ShowMessage("VIGALL::Execute", Versionstring, "Cannot run VIGALL from a History Template.");
                return;
            }


            if (!CheckHistoryNode(_analyzer.ActiveNode))
            {
                MessageDisplay.ShowMessage("VIGALL::Execute", Versionstring, "History Node check failed!");
                return;
            }

            if (_analyzer.ActiveNode.Dataset.Channels.Length < 25)
            {
                MessageDisplay.ShowMessage("VIGALL::Execute", Versionstring, "LORETA requires at least 25 channels. Please see the manual for details.");
                return;
            }

            if (_analyzer.ActiveNode.Dataset.SamplingInterval > 10000)
            {
                MessageDisplay.ShowMessage("VIGALL::Execute", Versionstring, "The sampling rate of this dataset is below 100Hz.\nVIGALL requires sampling rates between 100Hz and 512Hz.");
                return;
            }

            if (_analyzer.ActiveNode.Dataset.SamplingInterval < 1953)
            {
                MessageDisplay.ShowMessage("VIGALL::Execute", Versionstring, "The sampling rate of this dataset is above 512Hz.\nVIGALL requires sampling rates between 100Hz and 512Hz.");
                return;
            }

            for (var markercounter = 0; markercounter < _analyzer.ActiveNode.Dataset.Markers.Length; markercounter++)
                if ((_analyzer.ActiveNode.Dataset.Markers[markercounter].Type == "New Segment") && (_analyzer.ActiveNode.Dataset.Markers[markercounter].Position > 1))
                {
                    MessageDisplay.ShowMessage("VIGALL::Execute", Versionstring, "Please remove all New Segment markers before running VIGALL.");
                    return;
                }

            #endregion //Find Active Node

            #region Parameter Dialog Handling

            // Load parameters that were set the last time the dialog was closed with Ok, or load the built-in
            // defaults if the dialog was never called. Note the Guid that identifies the module.
            _para = TransformationParameters<Parameters>.LoadParametersDefault(new Guid(VigallId));

            using (var dlg = new FrmParameterDlg())
                {
                    dlg.Parameters = _para; // Set dialog parameters to the previous setting loaded above.

                    if (DialogDisplay.ShowAnalyzerDialog(dlg) != MessageResult.OK) // If dialog was canceled, exit AddIn.
                        return;

                    _para = dlg.Parameters; // Get changed parameters and save them as new default.
                    TransformationParameters<Parameters>.SaveParametersDefault(new Guid(VigallId), _para);
                }

            if (_para.batch) //if batch processing was selected in the parameter dialog, look for the batch to process
            {
                var nodescount = 0;

                _activeNodes = new IHistoryNode[_analyzer.HistoryFiles.Length];//might be less if the following search finds errors

                for (var c = 0; c < _analyzer.HistoryFiles.Length; c++)
                {
                    var nodes = _analyzer.HistoryFiles[c].FindNodes(_analyzer.ActiveNode.Name);

                    if (nodes.Length > 1)
                    {
                        _h = _h + _analyzer.HistoryFiles[c].Name + ": Node name " + _analyzer.ActiveNode.Name + " not unique" + Environment.NewLine;

                        _activeNodes[c] = null;
                    }
                    else //exactly one node of the right name found, referred to as nodes[0]
                    {
                        var error = "";

                        if (!CheckHistoryNode(nodes[0]))
                            error = "History Node check failed. ";

                        if (nodes[0].Dataset.Channels.Length < 25)
                            error = error + "Less than 25 channels. ";

                        if (nodes[0].Dataset.SamplingInterval > 10000)
                            error = error + "Sampling rate below 100 Hz. ";

                        if (nodes[0].Dataset.SamplingInterval <= 1953)
                            error = error + "Sampling rate above 512 Hz. ";

                        if ((!_para.purgeOldTempNodes)&&(nodes[0].HistoryFile.FindNodes("VIGALL Alpha").Length > 0))
                            error = error + "Node named 'VIGALL Alpha' found - please remove. ";

                        if ((!_para.purgeOldTempNodes)&&(nodes[0].HistoryFile.FindNodes("VIGALL DeltaTheta").Length > 0))
                            error = error + "Node named 'VIGALL DeltaTheta' found - please remove. ";

                        if ((!_para.purgeOldTempNodes)&&(_para.SEMdetect)&&(nodes[0].HistoryFile.FindNodes("VIGALL SEM Filters").Length > 0))
                            error = error + "Node named 'VIGALL SEM Filters' found - please remove. ";
                        
                        if ((!_para.purgeOldTempNodes)&&(nodes[0].HistoryFile.FindNodes("VIGALL LORETA Alpha").Length > 0))
                            error = error + "Node named 'VIGALL LORETA Alpha' found - please remove. ";

                        if ((!_para.purgeOldTempNodes)&&(nodes[0].HistoryFile.FindNodes("VIGALL LORETA DeltaTheta").Length > 0))
                            error = error + "Node named 'VIGALL LORETA DeltaTheta' found - please remove. ";

                        if ((_para.batchexclude)&&(nodes[0]["VIGALL"]!=null))
                            error = error + "Excluded due to previous VIGALL results node. ";

                        for (var markercounter = 0; markercounter < nodes[0].Dataset.Markers.Length; markercounter++)
                            if ((nodes[0].Dataset.Markers[markercounter].Type == "New Segment") && (nodes[0].Dataset.Markers[markercounter].Position > 1))
                            {
                                error = error + "Contains New Segment marker.";
                                break;
                            }

                        if (error != "") //if any error occurred
                        {
                            _h = _h + _analyzer.HistoryFiles[c].Name + ": " + error + Environment.NewLine;
                            _activeNodes[c] = null;
                        }
                        else //all preliminary checks passed
                        {
                            nodescount++;
                            _activeNodes[c] = nodes[0];
                        }
                    }
                }
                if (nodescount == 0) //if none can be processed, say so and cancel
                {
                    MessageDisplay.ShowMessage("VIGALL::Execute", Versionstring, "No valid target nodes found!" + Environment.NewLine + _h);
                    return;
                }

                var opennodescount = AutomationSupport.Application.HistoryFiles.Count(t1 => t1.IsOpen);
                //LINQ expression, creates a count of those AutomationSupport.Application.HistoryFiles where .IsOpen is true

                if (nodescount < opennodescount) //if less than all can be processed, ask user to confirm
                    if (MessageDisplay.AskOKCancel("VIGALL::Execute", Versionstring,
                       (opennodescount - nodescount) + " history files cannot be processed!" + Environment.NewLine
                       + Environment.NewLine + _h) != MessageResult.OK) return; //if not confirmed, cancel
            }

            //copy parameters so we won't need to access para in the main loop... probably unnecessary
            _absPowerThresholdA = _para.absThresholdA;
            _relThreshA = _para.relThresholdA;
            _relThreshA1 = _para.relThresholdA1;
            _relThreshA2 = _para.relThresholdA2;
            _absPowerThresholdB23 = _para.absThresholdB23;

            #endregion //Parameter Dialog Handling

            #region Outermost loop

            if (_para.batch)
            {
                var report = "";
                foreach (var t1 in _activeNodes)
                {
                    if (t1 != null)
                    {
                        var result = DoEverything(t1); //DoEverything() is where all the actual processing happens
                        _pb.Dispose();
                        if (result == "Limit of LORETA operations per batch processing reached.") //if LORETA failed (presumably due to lack of memory)
                        {
                            if (!_para.leaveTempNodes)
                            {
                                _alphaNode.Delete();
                                _deltathetaNode.Delete();
                                if (_para.SEMdetect)
                                    _seMfiltersNode.Delete();
                            }
                            break; //escape the for loop because if this happens, future LORETAs would fail too
                        }

                        /*This is necessary because the way this AddIn does batch processing is a crude hack, implemented after BrainProducts said AddIns could not do that.*/
                        /*Yes they can. But they run out of memory eventually, probably because memory doesn't get freed up until the AddIn terminates.*/

                        if (result != "")
                        {
                            report = report + t1.HistoryFile.Name + ": " + result + Environment.NewLine;
                        }
                    }
                }
                if(report!="")//if any errors occurred
                    MessageDisplay.ShowMessage("VIGALL::Execute", Versionstring + " Error Log", report);
            }
            else //no batch processing - just the selected node
            {
                //if intermediate nodes exist and have not been marked for removal
                if  ((!_para.purgeOldTempNodes)&&
                    ((_hf.FindNodes("VIGALL DeltaTheta").Length > 0)
                || (_hf.FindNodes("VIGALL Alpha").Length > 0)
                || (_hf.FindNodes("VIGALL SEM Filters").Length > 0)
                || (_hf.FindNodes("VIGALL LORETA Alpha").Length > 0)
                || (_hf.FindNodes("VIGALL LORETA DeltaTheta").Length > 0)))
                { //...ask user to delete them so they don't confuse the algorithm later
                    MessageDisplay.ShowMessage("VIGALL::Execute", Versionstring, "Since you did not choose to have previous temporary nodes removed, nodes with the following names must not exist in the history tree of the selected node:" +
                                                Environment.NewLine + " VIGALL Alpha" +
                                                Environment.NewLine + " VIGALL DeltaTheta" +
                                                Environment.NewLine + " VIGALL LORETA Alpha" +
                                                Environment.NewLine + " VIGALL LORETA DeltaTheta" +
                                                Environment.NewLine + " VIGALL SEM Filters");
                    return;
                }
                var result = DoEverything(_analyzer.ActiveNode);
                if (result != "")//if an error occurred
                {
                    MessageDisplay.ShowMessage("VIGALL::Execute", Versionstring + " Error", result);
                }
                //if no error occurred, there is no final message and the progress bar simply vanishes
                _pb.Dispose();
            }

            #endregion //Outermost loop
        } //end of Execute()

        private string DoEverything(IHistoryNode aNode) //this does the VIGALL proper
        {
            uint acTimeIndex = 0;

            string acf_output; //output of the alpha center frequency finder
            string erp_output = "";

            float[] frontalAlpha; //this is where we store the main eight types of data streams we're gonna use
            float[] occipitalAlpha; //yes a two-dimensional array would be more elegant, but this is more readable 
            float[] parietalAlpha;
            float[] temporalAlpha;
            float[] frontalDt;
            float[] occipitalDt;
            float[] parietalDt;
            float[] temporalDt;

            double ocAlMax = 0;//the highest occipital Alpha Power, used in adapting absolute power thresholds

            _pointsPerSecond = (uint)(1000000 / aNode.Dataset.SamplingInterval); //1000000 is the number of microseconds in a (1 second) segment

            #region Set up progress bar

            _titlestring = aNode.HistoryFile.Name + " - " + Versionstring;
            _pb = _analyzer.CreateProgressBar(_titlestring, "Preprocessing");
            //pb is created inside doEverything(), but disposed only after doEverything() returns

            _pb.SetRange(0, 7); //give or take...
            _pb.Step = 1;
            _pb.Show();
            _pb.StepIt(); //don't start empty

            #endregion Set up progress bar

            #region Set up results node

            // markers can only be erased or created in a new node, so resultsNode
            // needs to be up although at this point we're far from producing results
            _hf = aNode.HistoryFile; //main data structure, (almost) everything else derives from here

            _resultsNode = CreateOutputFile(aNode);
            if (_resultsNode == null) //If output file creation fails, exit add in.
                return "History file creation failed. Exiting.";

            #endregion //Set up results node

            #region Purge old temp nodes
            if (_para.purgeOldTempNodes)
            {
                _pb.Text = "Deleting temporary nodes";
                if (_hf.FindNodes("VIGALL Alpha").Length > 0) 
                    _hf.FindNodes("VIGALL Alpha")[0].Delete();
                if (_hf.FindNodes("VIGALL DeltaTheta").Length > 0)
                    _hf.FindNodes("VIGALL DeltaTheta")[0].Delete();
                if ((_para.SEMdetect) && (_hf.FindNodes("VIGALL SEM Filters").Length > 0))
                    _hf.FindNodes("VIGALL SEM Filters")[0].Delete();
                if (_hf.FindNodes("VIGALL LORETA Alpha").Length > 0)
                    _hf.FindNodes("VIGALL LORETA Alpha")[0].Delete();
                if (_hf.FindNodes("VIGALL LORETA DeltaTheta").Length > 0)
                    _hf.FindNodes("VIGALL LORETA DeltaTheta")[0].Delete();
                _hf.PurgeDeletedNodes();
            }
            #endregion //Purge old temp nodes

            #region Create standard markers environment
            //if VIGALL is run on a VIGALL-processed node, RR and SEM markers set by the previous VIGALL run need to be removed
            var mks = _resultsNode.Markers;
            var w = 0;
            while (w < mks.Length)
            {
                if ((mks[w].Description == "Bad RR")
                    || (mks[w].Description == "Slow Eye Movement"))
                    _resultsNode.RemoveMarker(mks[w].ChannelNumber, mks[w].Position, mks[w].Points, mks[w].Type, mks[w].Description);
                else
                    w++;
            }

            _pb.StepIt();
            #endregion //Create standard markers environment

            #region Preprocessing

            #region ACF
            /* this is a somewhat complicated bit that automates finding the center of the Alpha frequency range in this particular measurement*/
            /* it should probably be its own function or even its own class, but we're doing this once per node so it might as well be part of the main function*/

            _pb.Text = "Preprocessing: Finding individual alpha frequency"; //pb is the progress bar

            double largestIntegralYet = 0;
            if (_para.ACFauto)
            {
                acf_output = ""; //just in case we're batch processing

                /*we'll be working on the average of the O1 and O2 channels, so let's find them*/

                uint o1Nr = 1000;
                uint o2Nr = 1000;
                for (_i = 0; _i < aNode.Dataset.Channels.Length; _i++)
                    if (aNode.Dataset.Channels[(int)_i].Name == "O1")
                        o1Nr = _i;

                if (o1Nr == 1000)
                    return "Channel O1 not found!";

                for (_i = 0; _i < aNode.Dataset.Channels.Length; _i++)
                    if (aNode.Dataset.Channels[(int)_i].Name == "O2")
                        o2Nr = _i;
                if (o2Nr == 1000)
                    return "Channel O2 not found!";

                _o1 = aNode.Dataset.GetData(0, aNode.Dataset.Length, new[] { (int)o1Nr });
                _o2 = aNode.Dataset.GetData(0, aNode.Dataset.Length, new[] { (int)o2Nr });

                if ((_o1 == null) || (_o2 == null) || (_o1.Length != _o2.Length))
                    return "Found O1 and O2 channels, but could not read them!";

                /* we'll do fourier transforms on 10 second windows of the measurement, spaced out by 1 second increments, so first we calculate how many data points that is*/

                _pointsPerFt = (uint)(10000000 / aNode.Dataset.SamplingInterval); //10000000 ms = 10 seconds
                //at 100 Hz, this is 1000

                _pointsPerIncrement = (uint)(1000000 / aNode.Dataset.SamplingInterval);
                //at 100 Hz, this is 100

                _numberOfFt = (uint)(((_o1.Length - _pointsPerFt) / _pointsPerIncrement) + 1);
                /*this is the maximum number of transforms that could be performed*/
                /*now we look which will be skipped for being outside the search range or for containing artifacts or sleep markers*/

                var performFtHere = new bool[_numberOfFt];
                for (_i = 0; _i < performFtHere.Length; _i++)
                    if ((_i < _para.ACFfrom) || (_i > _para.ACFto))//not in the search range specified
                        performFtHere[_i] = false;
                    else
                        performFtHere[_i] = true;
                //in other words: perform_FT_here[i] = ((i >= para.ACFfrom) && (i <= para.ACFto));

                var mk = aNode.Dataset.Markers;

                for (_j = 0; _j < mk.Length; _j++)
                    if ((mk[_j].Type == "Bad Interval") ||
                         ((mk[_j].Type == "Comment") && ((mk[_j].Description == "C") || (mk[_j].Description == "K") || (mk[_j].Description == "S"))))
                    {
                        for (_u = (int)(mk[_j].Position / _pointsPerIncrement); _u <= (int)((mk[_j].Position + mk[_j].Points - 1) / _pointsPerIncrement); _u++)
                            for (var v = -9; v <= ((mk[_j].Points - 1) / _pointsPerIncrement); v++)//a 1 point artifact will usually render 10 candidate segments of 10s unusable
                                if (((_u + v) >= 0) && ((_u + v) < performFtHere.Length))
                                    performFtHere[_u + v] = false;
                    }

                _output = new float[_numberOfFt, 140]; //first index: number of FT, second index: frequency, content: amplitude

                acf_output = acf_output + "Not transformed (Out of Range, Bad Interval or C): ";
                var counter = _numberOfFt;
                if (_para.ACFto < _numberOfFt) counter = _para.ACFto;
                counter = counter - _para.ACFfrom;
                for (_j = 0; _j < _numberOfFt; _j++)
                    if (performFtHere[_j])
                    {
                        _pb.Text = "Preprocessing: Finding individual alpha frequency (" + _j + "/" + counter +" DFTs)";
                        MyDft(_j, 30, 140); //from 3 Hz to  14 Hz
                    }
                    else
                    {
                        acf_output = acf_output + (1 * _j) + "-" + (1 * (_j + 10)) + "s ";
                    }

                //at this point, output[,] contains the Fourier transform results

                _pb.Text = "Preprocessing: Finding individual alpha frequency";

                uint acFreqIndex = 0;

                double integral;
                acTimeIndex = 0;
                uint freq; //individual frequency, not in Hz but as an index of output[]
                double sAmp; //sum of amplitudes, NOT the amplitude at the center frequency

                acf_output = acf_output + "Ignored (Delta+Theta >= Alpha): ";

                for (_i = 0; _i < _numberOfFt; _i++)
                    if (performFtHere[_i])
                    {
                        integral = 0;
                        sAmp = 0;

                        double deltathetaAmp = 0;
                        for (var u = 30; u < 65; u++)
                            deltathetaAmp += _output[_i, u];
                        deltathetaAmp /= 35; //divide by number of frequencies included to obtain the average

                        for (var u = 80; u < 140; u++)
                        {
                            sAmp += _output[_i, u];
                            integral += (u * _output[_i, u]);
                        }
                        freq = (uint)(integral / sAmp); //divide by sum of amplitudes to obtain the center frequency (again not in Hz but as an index in output[])

                        if (deltathetaAmp < _output[_i, freq]) //DT < A so the subject is probably awake so this FT will be looked at
                        {
                            if (integral > largestIntegralYet)
                            {
                                acTimeIndex = _i;
                                largestIntegralYet = integral;
                            }
                        }
                        else //probably asleep, so we ignore this FT
                        {
                            acf_output = acf_output + (1 * _i) + "-" + (1 * (_i + 10)) + "s ";
                        }

                    }

                //now ac_time_index is the index of the one FT that contains most alpha power
                //within this one, we'll now look for the one 2Hz window that contains the largest alpha power integral

                largestIntegralYet = 0;
                for (var u = 80; u < 120; u++)
                {
                    integral = 0;
                    for (var v = 0; v < 20; v++)
                        integral += _output[acTimeIndex, u + v];

                    if (integral > largestIntegralYet)
                    {
                        acFreqIndex = (uint)u;
                        largestIntegralYet = integral;
                    }
                }
                //now ac_freq_index is the one 2Hz window that contains most alpha power in the selected FT
                //this is what we want the center frequency of

                integral = 0;
                sAmp = 0;
                for (var v = 0; v < 20; v++)
                {
                    sAmp += _output[acTimeIndex, acFreqIndex + v];
                    integral += ((acFreqIndex + v) * _output[acTimeIndex, acFreqIndex + v]);
                }
                freq = (uint)(integral / sAmp); //divide by sum of amplitudes to obtain center frequency (an index in output[])



                acf_output = acf_output + Environment.NewLine;

                if (freq != 0)
                {
                    acf_output = acf_output + "Individual Alpha Frequency: " + ((float)(freq) / 10) + " Hz - Located at " + (1 * acTimeIndex) + " - " + ((1 * acTimeIndex) + 10) + " s." + Environment.NewLine;

                    _para.alphacenter = (float)(freq) / 10;
                }
                else
                    return "Error in detection of individual alpha frequency!";

            }
            else
                acf_output = Environment.NewLine + "Individual alpha frequency finder not used. Frequency used: " + _para.alphacenter + " Hz"+ Environment.NewLine;
            #endregion //APF

            _segs = aNode.Dataset.Length / _pointsPerSecond; //calculate number of segments in the dataset

            _t = _segs * _pointsPerSecond;
            //if aNode.Dataset.Length / pointsPerSecond is not integer, t becomes aNode.Dataset.Length minus the last few points
            //so basically t is "points till end of last segment", but that's too long

            var sstate = new bool[_segs]; // here we save SEM states
            var state = new string[_segs]; // here we save the classifications
            var statelong = new string[_segs]; //here we'll store more extensive info on each segment (including power values)

            var csv = new string[_segs,21];//new and better way to store final results

            for (_i = 0; _i < _segs; _i++) 
                sstate[_i] = false;

            string result;
            if (_para.SEMdetect)
            {
                result = DetectSem(aNode); //if SEMs are to be detected but detectSEM() fails...
                if (result != "")
                    return result;//...doEverything fails for this node
            }

            //this is where most preprocessing happens
            result = create_and_initialize_main_temp_nodes(aNode);
            if (result != "") //if create_and_initialize_main_temp_nodes() fails
                return result;

            #region use_AC_to_modify_absolute_criteria
            /*since we now know the subject's typical Alpha, we can make informed assumption about what amount of Alpha power means she's in an A state*/
            if ((_para.ACFauto) && (_para.adaptAbsThresholds))
            {
                ocAlMax = 0;
                //uint pointsPerSecond = (uint)(1000000 / aNode.Dataset.SamplingInterval);
                occipitalAlpha = _alphaNode.Dataset.GetData(acTimeIndex * _pointsPerSecond, 10 * _pointsPerSecond, new[] { 2 });

                for (_i = 0; _i < 10 * _pointsPerSecond; _i++)
                    ocAlMax += occipitalAlpha[_i] * 100000000000 / _pointsPerSecond; //scale the same way power values are scaled below

                ocAlMax /= 10; //this is from a 10 seconds interval, so we divide it to compare with 1 second intervals

                var fraction = (Math.Log(150000) - Math.Log(50000)) / (Math.Log(4500000) - Math.Log(40000));
                //this is in a seperate line only to make later changes easier

                _absPowerThresholdA = Math.Exp((fraction * (Math.Log(ocAlMax) - Math.Log(40000))) + Math.Log(50000));

                var rounder = (int)_absPowerThresholdA;
                _absPowerThresholdA = rounder;
                _absPowerThresholdB23 = _absPowerThresholdA * _para.absThresholdsFactor;
                rounder = (int)ocAlMax;
                ocAlMax = rounder;
            }

            #endregion //useACFtomodifyB23

            //this finds SEM markers set by mySEM2() and puts 'true' into sstate[]s touched by these markers
            for (w = 0; w < _resultsNode.Markers.Length; w++) //w counts markers
            {
                if (_resultsNode.Markers[w].Description == "Slow Eye Movement")
                {
                    _i = 0;
                    uint semStart = _resultsNode.Markers[w].Position;
                    uint semLength = _resultsNode.Markers[w].Points;
                    while ((_i < _segs) && (_pointsPerSecond < semStart))
                    {
                        semStart = semStart - _pointsPerSecond;
                        _i++;
                    }//now i is the number of the segment where the SEM begins

                    if (_i < _segs)
                    {
                        sstate[_i] = true;
                        while ((_i < _segs) && (_pointsPerSecond < semLength))
                        {
                            semLength = semLength - _pointsPerSecond;
                            sstate[_i] = true;
                            _i++;
                        }//now i is the number of the segment where the SEM ends
                        sstate[_i] = true;
                    }
                }
            }

            #endregion //Preprocessing

            #region Actual VIGALL Operation

            #region ECG processing
            /*the electrocardiogram is not used in vigilance classification, but we calculate heart rate intervals here because there do tend to be correlations between heart rate intervals and vigilance
            and this allows researchers to comfortably check if that's the case*/

            var pulseLength = new uint[_segs]; //this array is to be filled with each segment's (estimated) heart rate interval
            if (_para.RRdetect) //else pulseLength remains full of zeroes
            {
                _pb.Text = "Calculating heart rate...";

                mks = _resultsNode.Markers;

                //the algorithm works like this: it finds the last pulse before the beginning of the segment and the first pulse
                //after the end of the segment, then divides this distance by the number of pulses in it

                for (uint segCount = 0; segCount < _segs; segCount++)
                {
                    var startSeg = segCount * _pointsPerSecond;
                    var endSeg = (segCount+1) * _pointsPerSecond;
                    uint startPulse = 0;//will become position of last Pulse Artifact before beginning of segment
                    var endPulse = aNode.Dataset.Length; //and position of first Pulse Artifact after end of segment
                    uint pulseCount = 0; //will become number of Pulse Artifacts in between

                    for (w = 0; w < mks.Length; w++)
                    {
                        if (mks[w].Type == "Pulse Artifact")
                        {
                            if (mks[w].Position < startSeg) //if the marker is before the segment in question
                                if (mks[w].Position > startPulse) //if its position is after the previously stored startPulse
                                    startPulse = mks[w].Position; //it becomes the new startPulse

                            if (mks[w].Position > endSeg) //if the marker is after the segment in question
                                if (mks[w].Position < endPulse) //if its position is before the previously stored endPulse
                                    endPulse = mks[w].Position; //it becomes the new endPulse

                            if ((mks[w].Position >= startSeg) && (mks[w].Position <= endSeg))//if the marker is within the segment
                                pulseCount++;
                        }
                        pulseLength[segCount] = (uint)((endPulse - startPulse) * (aNode.Dataset.SamplingInterval / 1000)) / (pulseCount + 1);
                    }
                }

                _pos = 0;

                int ecgnr; //we get the number that m_Parameters.ECGname refers to, because resultsNode.AddMarker requires it
                for (ecgnr = 0; ecgnr < aNode.Dataset.Channels.Length; ecgnr++)
                    if (aNode.Dataset.Channels[ecgnr].Name == _para.ECGname) break;

                for (uint segCount = 0; segCount < _segs; segCount++) //implausible Heart Rate -> Bad Interval
                {
                    if ((pulseLength[segCount] < _para.minHeartrate) || (pulseLength[segCount] > _para.maxHeartrate))
                        _resultsNode.AddMarker(ecgnr, _pos, _pointsPerSecond, "Comment", "RR: " + pulseLength[segCount] + " ms?");
                    _pos += _pointsPerSecond;
                }
            }
            _pb.StepIt();
            #endregion //ECG processing

            #region Tag unclassifiable segments
            _pb.Text = "Tagging unclassifiable segments";

            mks = _resultsNode.Markers; //unlike aNode, resultsNode contains the markers that VIGALL has placed previously

            for (w = 0; w < mks.Length; w++) //this does nearly the same thing as the SEM markers to "true" entries in sstate[] (see above)
                if (mks[w].Type == "Bad Interval")
                {
                    state[(int)(mks[w].Position / _pointsPerSecond)] = "X";

                    if (mks[w].Points > 1)
                        for (_u = (int)(mks[w].Position / _pointsPerSecond); _u < (int)((mks[w].Position + mks[w].Points) / _pointsPerSecond); _u++)
                            state[_u] = "X";
                }

            #endregion //Tag unclassifiable segments

            #region Main Loop

            float a0Count;
            a0Count = 0;
            float a1Count;
            a1Count = 0;
            float a2Tcount;
            a2Tcount = 0;
            float a2Pcount;
            a2Pcount = 0;
            float a3Count;
            a3Count = 0;
            float b1Count;
            b1Count = 0;
            float b23Count;
            b23Count = 0;
            float ccount;
            ccount = 0;
            float xcount;
            xcount = 0;

            float scl;
            scl = 0;
            float temp;
            temp = 0;


            var tempNr = 0; //same thing as before, for para.TEMPname
            if (!_para.erp)
            {

                _pb.Step = 1;
                _pos = 0;

                var sclNr = 0; //find the number to para.SCLEDAname so we can access the dataset
                if (_para.SCLEDAdetect)
                    for (sclNr = aNode.Dataset.Channels.Length - 1; sclNr > -1; sclNr--)
                        if (aNode.Dataset.Channels[sclNr].Name == _para.SCLEDAname) break;

                if (_para.TEMPdetect)
                    for (tempNr = aNode.Dataset.Channels.Length - 1; tempNr > -1; tempNr--)
                        if (aNode.Dataset.Channels[tempNr].Name == _para.TEMPname) break;



                _pb.SetRange(0, (int)_segs); //reset progress bar to show the number of classified segments
                _pb.Position = 0;

                //for each segment
                for (uint s = 0; s < _segs; s++)
                {
                    //one step on the progress bar
                    _pb.Text = "Classifying segment " + (s + 1) + " of " + _segs;
                    _pb.StepIt();

                    if (state[s] == "X") //simplest case: bad segment
                    {
                        xcount++;
                        /*Marker*/
                        //resultsNode.AddMarker(-1, pos, pointsPerSegment, "Comment", "X", true);
                        /*Outputline*/
                        statelong[s] = "X " + scl + " " + temp + " " + pulseLength[s];
                        /*CSV*/
                        csv[s, 0] = (s+1).ToString(CultureInfo.InvariantCulture);
                        csv[s, 1] = "X";
                        csv[s, 2] = scl.ToString(CultureInfo.InvariantCulture);
                        csv[s, 3] = temp.ToString(CultureInfo.InvariantCulture);
                        csv[s, 4] = pulseLength[s].ToString(CultureInfo.InvariantCulture);
                        /*go on*/
                        _pos = _pos + _pointsPerSecond;
                    }
                    else
                    {
                        float frAl; //the names of these are abbreviated so the decision tree is human readable
                        frAl = 0;
                        float ocAl;
                        ocAl = 0;
                        float tmAl;
                        tmAl = 0;
                        float paAl;
                        paAl = 0;
                        float frDt;
                        frDt = 0;
                        float ocDt;
                        ocDt = 0;
                        float paDt;
                        paDt = 0;
                        float tmDt;
                        tmDt = 0;

                        float accAl;
                        accAl = 0;
                        float accDt;
                        accDt = 0;
                        float fraccAl;
                        fraccAl = 0;
                        float fraccDt;
                        fraccDt = 0;

                        //these are currently unused, they're part of the old version of the decision tree which we keep around for spare parts
                        float frDe;
                        frDe = 0;
                        float ocDe;
                        ocDe = 0;
                        float paDe;
                        paDe = 0;
                        float tmDe;
                        tmDe = 0;
                        float frTh;
                        frTh = 0;
                        float ocTh;
                        ocTh = 0;
                        float paTh;
                        paTh = 0;
                        float tmTh;
                        tmTh = 0;

                        //sum together all power values inside this segment, for each of the 4x4 arrays
                        frontalAlpha = _alphaNode.Dataset.GetData(_pos, _pointsPerSecond, new[] { 0 });
                        for (_i = 0; _i < _pointsPerSecond; _i++)
                            frAl += frontalAlpha[_i];
                        parietalAlpha = _alphaNode.Dataset.GetData(_pos, _pointsPerSecond, new[] { 1 });
                        for (_i = 0; _i < _pointsPerSecond; _i++)
                            paAl += parietalAlpha[_i];
                        occipitalAlpha = _alphaNode.Dataset.GetData(_pos, _pointsPerSecond, new[] { 2 });
                        for (_i = 0; _i < _pointsPerSecond; _i++)
                            ocAl += occipitalAlpha[_i];
                        temporalAlpha = _alphaNode.Dataset.GetData(_pos, _pointsPerSecond, new[] { 3 });
                        for (_i = 0; _i < _pointsPerSecond; _i++)
                            tmAl += temporalAlpha[_i];

                        frontalDt = _deltathetaNode.Dataset.GetData(_pos, _pointsPerSecond, new[] { 0 });
                        for (_i = 0; _i < _pointsPerSecond; _i++)
                            frDt += frontalDt[_i];
                        parietalDt = _deltathetaNode.Dataset.GetData(_pos, _pointsPerSecond, new[] { 1 });
                        for (_i = 0; _i < _pointsPerSecond; _i++)
                            paDt += parietalDt[_i];
                        occipitalDt = _deltathetaNode.Dataset.GetData(_pos, _pointsPerSecond, new[] { 2 });
                        for (_i = 0; _i < _pointsPerSecond; _i++)
                            ocDt += occipitalDt[_i];
                        temporalDt = _deltathetaNode.Dataset.GetData(_pos, _pointsPerSecond, new[] { 3 });
                        for (_i = 0; _i < _pointsPerSecond; _i++)
                            tmDt += temporalDt[_i];

                        frAl = frAl / _pointsPerSecond * 100000000000;//division to derive comparable values from data with varying sampling rates
                        ocAl = ocAl / _pointsPerSecond * 100000000000;//and multiplication for human readability
                        paAl = paAl / _pointsPerSecond * 100000000000;
                        tmAl = tmAl / _pointsPerSecond * 100000000000;
                        frDt = frDt / _pointsPerSecond * 100000000000;
                        ocDt = ocDt / _pointsPerSecond * 100000000000;
                        paDt = paDt / _pointsPerSecond * 100000000000;
                        tmDt = tmDt / _pointsPerSecond * 100000000000;

                        accAl  =  accAl / _pointsPerSecond * 100000000000;
                        fraccAl = fraccAl / _pointsPerSecond * 100000000000;
                        accDt  =  accDt / _pointsPerSecond * 100000000000;
                        fraccDt = fraccDt / _pointsPerSecond * 100000000000;

                        if (_para.SCLEDAdetect) //extra channel for SCL or EDA (not used in classification)
                        {
                            float[] sclA = aNode.Dataset.GetData(_pos, _pointsPerSecond, new[] { sclNr });
                            for (_i = 0; _i < _pointsPerSecond; _i++)
                                scl += sclA[_i];
                            scl = scl / _pointsPerSecond;
                        }
                        if (_para.TEMPdetect) //extra channel for temperature (not used in classification)
                        {
                            var tempA = aNode.Dataset.GetData(_pos, _pointsPerSecond, new[] { tempNr });
                            for (_i = 0; _i < _pointsPerSecond; _i++)
                                temp += tempA[_i];
                            temp = temp / _pointsPerSecond;
                        }

                        //the central classification decision tree, working with the 4x4 variables summed together above
                        //note that X and C classifications are extra (X before, C after)

                        if (_para.new_tree)
                        {
                            if (!(((ocAl < _absPowerThresholdA)//absolute criterion never exceeded
                                && (paAl < _absPowerThresholdA)
                                && (tmAl < _absPowerThresholdA)
                                && (frAl < _absPowerThresholdA))
                                || ((ocAl < (ocDt * _relThreshA)) //or relative criterion never exceeded
                                && (paAl < (paDt * _relThreshA))
                                && (tmAl < (tmDt * _relThreshA))
                                && (frAl < (frDt * _relThreshA)))))

                                if ((ocAl > paAl * _relThreshA1) && (ocAl > tmAl * _relThreshA1) && (ocAl > frAl * _relThreshA1))
                                    { state[s] = "A1"; a1Count++; }

                                else // i.e. ((ocAl < paAl * _relThreshA1) || (ocAl < tmAl * _relThreshA1) || (ocAl < frAl * _relThreshA1))
                                    if ((tmAl > frAl * _relThreshA2) || (paAl > frAl * _relThreshA2))
                                        if (_para.a2pa2t)
                                            if (tmAl > paAl)
                                            { state[s] = "A2T"; a2Tcount++; }
                                            else //(tmAl <= paAl)
                                            { state[s] = "A2P"; a2Pcount++; }
                                        else//(!para.a2pa2t)
                                            { state[s] = "A2"; a2Pcount++; }
                                    else //((tmAl < frAl * relThreshA2) && (paAl < frAl * relThreshA2))
                                        { state[s] = "A3"; a3Count++; }

                            else //means it is non-A
                            {
                                if ((ocDt > _absPowerThresholdB23)//absolute power threshold B2/3 not exceeded in any ROI
                                    || (paDt > _absPowerThresholdB23)
                                    || (tmDt > _absPowerThresholdB23)
                                    || (frDt > _absPowerThresholdB23))
                                { state[s] = "B23"; b23Count++; }
                                else
                                    if ((_para.SEMdetect == false) || (sstate[s]))//if SEMs are disregarded or found
                                    { state[s] = "B1"; b1Count++; }
                                    else //if SEMs are regarded and not found
                                    { state[s] = "0"; a0Count++; }
                            }
                        }
                        else
                        {// the old decision tree - this part of the code is deprecated and unused, but kept for reference
                            if (((ocTh + ocDe + ocAl) < _absPowerThresholdA * _pointsPerSecond)//if the EEG is not (very) desynchronized, B1 or 0
                                && ((paTh + paDe + paAl) < _absPowerThresholdA * _pointsPerSecond)
                                && ((tmTh + tmDe + tmAl) < _absPowerThresholdA * _pointsPerSecond)
                                && ((frTh + frDe + frAl) < _absPowerThresholdA * _pointsPerSecond))
                            {
                                if ((_para.SEMdetect == false) || (state[s] == "S"))//if SEMs are disregarded or found
                                {
                                    state[s] = "B1"; b1Count++;
                                }
                                else //if SEMs are regarded and not found
                                {
                                    state[s] = "0"; a0Count++;
                                }
                            }

                            else //synchronized and
                                if ((ocAl / (ocDt + ocDe + ocTh) > _relThreshA) //mostly Alpha
                                    || (paAl / (paDt + paDe + paTh) > _relThreshA)
                                    || (tmAl / (tmDt + tmDe + tmTh) > _relThreshA)
                                    || (frAl / (frDt + frDe + frTh) > _relThreshA))

                                    if ((ocAl > paAl * _relThreshA1) && (ocAl > tmAl * _relThreshA1) && (ocAl > frAl * _relThreshA1))

                                    { state[s] = "A1"; a1Count++; }

                                    else // i.e. !((ocAl > paAl * a1factor) && (ocAl > tmAl * a1factor) && (ocAl > frAl * a1factor))
                                        if ((tmAl > frAl * _relThreshA2) && (paAl > frAl * _relThreshA2))

                                            if (tmAl > paAl)
                                            { state[s] = "A2T"; a2Tcount++; }
                                            else
                                            { state[s] = "A2P"; a2Pcount++; }

                                        else { state[s] = "A3"; a3Count++; }

                                else { state[s] = "B23"; b23Count++; }

                        }

                        statelong[s] = state[s] + " " + scl + " " + temp + " " + pulseLength[s] + " "
                                   + ocAl + " " + ocDt + " " + accAl + " " + accDt+ " "
                                   + paAl + " " + paDt + " " + fraccAl + " " + fraccDt+ " "
                                   + tmAl + " " + tmDt + " 0 0 "
                                   + frAl + " " + frDt + " 0 ";

                        csv[s,0] = (s + 1).ToString(CultureInfo.InvariantCulture);
                        csv[s,1] = state[s];
                        csv[s,2] = scl.ToString(CultureInfo.InvariantCulture);
                        csv[s,3] = temp.ToString(CultureInfo.InvariantCulture);
                        csv[s,4] = pulseLength[s].ToString(CultureInfo.InvariantCulture);
                        csv[s, 5] = ocAl.ToString(CultureInfo.InvariantCulture);
                        csv[s, 6] = ocDt.ToString(CultureInfo.InvariantCulture);
                        csv[s, 7] = accAl.ToString(CultureInfo.InvariantCulture);
                        csv[s, 8] = accDt.ToString(CultureInfo.InvariantCulture);
                        csv[s, 9] = paAl.ToString(CultureInfo.InvariantCulture);
                        csv[s, 10] = paDt.ToString(CultureInfo.InvariantCulture);
                        csv[s, 11] = fraccAl.ToString(CultureInfo.InvariantCulture);
                        csv[s, 12] = fraccDt.ToString(CultureInfo.InvariantCulture);
                        csv[s, 13] = tmAl.ToString(CultureInfo.InvariantCulture);
                        csv[s, 14] = tmDt.ToString(CultureInfo.InvariantCulture);
                        csv[s,15] = "0";
                        csv[s,16] = "0";
                        csv[s, 17] = frAl.ToString(CultureInfo.InvariantCulture);
                        csv[s, 18] = frDt.ToString(CultureInfo.InvariantCulture);
                        csv[s,19] = "0";


                        if (sstate[s])
                        {
                            statelong[s] = statelong[s] + "1";
                            csv[s,20] = "1";
                        }
                        else
                        {
                            statelong[s] = statelong[s] + "0";
                            csv[s,20] = "0";
                        }
                        /*go on*/
                        _pos = _pos + _pointsPerSecond;
                    }
                }

            #endregion //Main Loop

                /*Stage C*/
                //this part of the algorithm uses state[] and "C"-markers to modify state[] and statelong[]
                //it is not elegant
                for (_j = 0; _j < mks.Length; _j++)
                {
                    if ((mks[_j].Type == "Comment") && ((mks[_j].Description == "C") || (mks[_j].Description == "K") || (mks[_j].Description == "S")))
                    {
                        _u = (int)((mks[_j].Position - (mks[_j].Position % _pointsPerSecond)) / _pointsPerSecond);

                        switch (state[_u])//replace whatever is currently in state[u+w] and statelong[u+w]
                        {
                            case "B1":
                                statelong[_u] = statelong[_u].Replace("B1", "C");//not elegant! but it works
                                csv[_u, 1] = "C";
                                b1Count--;
                                ccount++;
                                break;
                            case "B23":
                                statelong[_u] = statelong[_u].Replace("B23", "C");
                                csv[_u, 1] = "C";
                                b23Count--;
                                ccount++;
                                break;
                            case "A1":
                                statelong[_u] = statelong[_u].Replace("A1", "C");
                                csv[_u, 1] = "C";
                                a1Count--;
                                ccount++;
                                break;
                            case "A2P":
                                statelong[_u] = statelong[_u].Replace("A2P", "C");
                                csv[_u, 1] = "C";
                                a2Pcount--;
                                ccount++;
                                break;
                            case "A2T":
                                statelong[_u] = statelong[_u].Replace("A2T", "C");
                                csv[_u, 1] = "C";
                                a2Tcount--;
                                ccount++;
                                break;
                            case "A2":
                                statelong[_u] = statelong[_u].Replace("A2", "C");
                                csv[_u, 1] = "C";
                                a2Pcount--;
                                ccount++;
                                break;
                            case "A3":
                                statelong[_u] = statelong[_u].Replace("A3", "C");
                                csv[_u, 1] = "C";
                                a3Count--;
                                ccount++;
                                break;
                            case "0":
                                statelong[_u] = "C" + statelong[_u].Substring(1);
                                csv[_u, 1] = "C";
                                a0Count--;
                                ccount++;
                                break;
                        }

                        state[_u] = "C"; //the segment with the marker in it ALWAYS becomes C... we assume the person who put the marker there knows what she's doing

                        w = 1;
                        while (w < (int)_para.stageClength)
                        {
                            if (_u + w == _segs) break; //if the C stage exceeds the data length, stop
                            switch (state[_u + w])//replace whatever is currently in state[u+w], statelong[u+w] and csv[u+w, 1]
                            {
                                case "B1":
                                    state[_u + w] = "C";
                                    statelong[_u + w] = statelong[_u + w].Replace("B1", "C");//not elegant! but it works
                                    csv[_u + w, 1] = "C";
                                    b1Count--;
                                    ccount++;
                                    w++;
                                    break;
                                case "B23":
                                    state[_u + w] = "C";
                                    statelong[_u + w] = statelong[_u + w].Replace("B23", "C");
                                    csv[_u + w, 1] = "C";
                                    b23Count--;
                                    ccount++;
                                    w++;
                                    break;
                                case "A1":
                                    w = (int)_para.stageClength;
                                    break;
                                case "A2P":
                                    w = (int)_para.stageClength;
                                    break;
                                case "A2T":
                                    w = (int)_para.stageClength;
                                    break;
                                case "A2":
                                    w = (int)_para.stageClength;
                                    break;
                                case "A3":
                                    w = (int)_para.stageClength;
                                    break;
                                case "0":
                                    state[_u + w] = "C";
                                    statelong[_u + w] = "C" + statelong[_u + w].Substring(1);
                                    csv[_u + w, 1] = "C";
                                    a0Count--;
                                    ccount++;
                                    w++;
                                    break;
                                default: //X
                                    w++;
                                    break;

                            }
                        }
                    }
                }
            }//(!para.erp) 

            /* event related potential analysis */
            else //i.e. (para.erp) is true
            {
                erp_output = "Event Related Potentials: classifying " + _para.erp_before + "ms before to " + _para.erp_after + "ms after Comment markers named '" + _para.erp_description + "'." + Environment.NewLine;
                _pb.Text = "ERP analysis";

                /*(1000000 / aNode.Dataset.SamplingInterval) are points per second, so time_before_ERP and time_after_ERP are intervals in points*/
                var time_before_ERP = (uint)(((float)_para.erp_before / 1000) * _pointsPerSecond);
                var time_after_ERP = (uint)(((float)_para.erp_after / 1000) * _pointsPerSecond);
                var erp_state = "";
                var resultscounter = 0;

                for (_j = 0; _j < mks.Length; _j++)
                {
                    if (mks[_j].Description == _para.erp_description)
                    {

                        if (time_before_ERP > mks[_j].Position) //testen!
                        {
                            erp_output = erp_output + "Marker placed too close to beginning of dataset!";
                            break;
                        }

                        _pos = mks[_j].Position - time_before_ERP;

                        if ((mks[_j].Position + time_after_ERP) > _alphaNode.Dataset.Length) //testen!
                        {
                            erp_output = erp_output + "Marker placed too close to end of dataset!";
                            break;
                        }

                        erp_state = ""; //this is where we store the ERP analog to the segment classification

                        foreach (var t1 in mks)
                        {
                            if (t1.Type == "Bad Interval")//this overlaps with the erp interval if any of the following three is true
                                if ((mks[_j].Position == t1.Position)//they're in the same place
                                    || (((mks[_j].Position - time_before_ERP) < t1.Position) && ((mks[_j].Position + time_after_ERP) > t1.Position)) //bad interval begins within ERP interval
                                    || ((t1.Position < mks[_j].Position - time_before_ERP) && ((t1.Position + t1.Points) > mks[_j].Position - time_before_ERP))) //ERP interval begins within bad interval
                                { erp_state = "X"; xcount++; }
                        }

                        float frAl = 0; //the names of these are abbreviated so the decision tree is more readable
                        float ocAl = 0;
                        float tmAl = 0;
                        float paAl = 0;
                        float frDt = 0;
                        float ocDt = 0;
                        float paDt = 0;
                        float tmDt = 0;

                        frontalAlpha = _alphaNode.Dataset.GetData(_pos, time_before_ERP + time_after_ERP, new[] { 0 });
                        for (_i = 0; _i < (time_before_ERP + time_after_ERP); _i++)
                            frAl += frontalAlpha[_i];
                        parietalAlpha = _alphaNode.Dataset.GetData(_pos, time_before_ERP + time_after_ERP, new[] { 1 });
                        for (_i = 0; _i < (time_before_ERP + time_after_ERP); _i++)
                            paAl += parietalAlpha[_i];
                        occipitalAlpha = _alphaNode.Dataset.GetData(_pos, time_before_ERP + time_after_ERP, new[] { 2 });
                        for (_i = 0; _i < (time_before_ERP + time_after_ERP); _i++)
                            ocAl += occipitalAlpha[_i];
                        temporalAlpha = _alphaNode.Dataset.GetData(_pos, time_before_ERP + time_after_ERP, new[] { 3 });
                        for (_i = 0; _i < (time_before_ERP + time_after_ERP); _i++)
                            tmAl += temporalAlpha[_i];

                        frontalDt = _deltathetaNode.Dataset.GetData(_pos, time_before_ERP + time_after_ERP, new[] { 0 });
                        for (_i = 0; _i < (time_before_ERP + time_after_ERP); _i++)
                            frDt += frontalDt[_i];
                        parietalDt = _deltathetaNode.Dataset.GetData(_pos, time_before_ERP + time_after_ERP, new[] { 1 });
                        for (_i = 0; _i < (time_before_ERP + time_after_ERP); _i++)
                            paDt += parietalDt[_i];
                        occipitalDt = _deltathetaNode.Dataset.GetData(_pos, time_before_ERP + time_after_ERP, new[] { 2 });
                        for (_i = 0; _i < (time_before_ERP + time_after_ERP); _i++)
                            ocDt += occipitalDt[_i];
                        temporalDt = _deltathetaNode.Dataset.GetData(_pos, time_before_ERP + time_after_ERP, new[] { 3 });
                        for (_i = 0; _i < (time_before_ERP + time_after_ERP); _i++)
                            tmDt += temporalDt[_i];

                        /*                    frontalDelta = deltaNode.Dataset.GetData(pos, EKP_length, new int[] { 0 });
                                            for (i = 0; i < EKP_length; i++)
                                                frDe += frontalDelta[i];
                                            parietalDelta = deltaNode.Dataset.GetData(pos, EKP_length, new int[] { 1 });
                                            for (i = 0; i < EKP_length; i++)
                                                paDe += parietalDelta[i];
                                            occipitalDelta = deltaNode.Dataset.GetData(pos, EKP_length, new int[] { 2 });
                                            for (i = 0; i < EKP_length; i++)
                                                ocDe += occipitalDelta[i];
                                            temporalDelta = deltaNode.Dataset.GetData(pos, EKP_length, new int[] { 3 });
                                            for (i = 0; i < EKP_length; i++)
                                                tmDe += temporalDelta[i];

                                            frontalTheta = thetaNode.Dataset.GetData(pos, EKP_length, new int[] { 0 });
                                            for (i = 0; i < EKP_length; i++)
                                                frTh += frontalTheta[i];
                                            parietalTheta = thetaNode.Dataset.GetData(pos, EKP_length, new int[] { 1 });
                                            for (i = 0; i < EKP_length; i++)
                                                paTh += parietalTheta[i];
                                            occipitalTheta = thetaNode.Dataset.GetData(pos, EKP_length, new int[] { 2 });
                                            for (i = 0; i < EKP_length; i++)
                                                ocTh += occipitalTheta[i];
                                            temporalTheta = thetaNode.Dataset.GetData(pos, EKP_length, new int[] { 3 });
                                            for (i = 0; i < EKP_length; i++)
                                                tmTh += temporalTheta[i];*/

                        frAl = frAl / (time_before_ERP + time_after_ERP) * 100000000000; //normalize for values to be comparable to usual 1 second segments
                        ocAl = ocAl / (time_before_ERP + time_after_ERP) * 100000000000;
                        paAl = paAl / (time_before_ERP + time_after_ERP) * 100000000000;
                        tmAl = tmAl / (time_before_ERP + time_after_ERP) * 100000000000;
                        frDt = frDt / (time_before_ERP + time_after_ERP) * 100000000000;
                        ocDt = ocDt / (time_before_ERP + time_after_ERP) * 100000000000;
                        paDt = paDt / (time_before_ERP + time_after_ERP) * 100000000000;
                        tmDt = tmDt / (time_before_ERP + time_after_ERP) * 100000000000;
                        /*                    frDe = frDe * 1000000000 / EKP_length * pointsPerSegment;
                                            ocDe = ocDe * 1000000000 / EKP_length * pointsPerSegment;
                                            paDe = paDe * 1000000000 / EKP_length * pointsPerSegment;
                                            tmDe = tmDe * 1000000000 / EKP_length * pointsPerSegment;
                                            frTh = frTh * 1000000000 / EKP_length * pointsPerSegment;
                                            ocTh = ocTh * 1000000000 / EKP_length * pointsPerSegment;
                                            paTh = paTh * 1000000000 / EKP_length * pointsPerSegment;
                                            tmTh = tmTh * 1000000000 / EKP_length * pointsPerSegment;*/

                        if (erp_state == "") //i.e. not X
                        {

                            if (((ocAl < _absPowerThresholdA)//the absolute criterion has never been exceeded...
                                && (paAl < _absPowerThresholdA)
                                && (tmAl < _absPowerThresholdA)
                                && (frAl < _absPowerThresholdA))
                                || ((ocAl < (ocDt * _relThreshA)) //or the relative criterion has never been exceeded...
                                && (paAl < (paDt * _relThreshA))
                                && (tmAl < (tmDt * _relThreshA))
                                && (frAl < (frDt * _relThreshA))))//...means it is not an A segment
                            {
                                if ((ocDt > _absPowerThresholdB23)//if the absolute criterion was never exceeded it is B23
                                    || (paDt > _absPowerThresholdB23)
                                    || (tmDt > _absPowerThresholdB23)
                                    || (tmDt > _absPowerThresholdB23))
                                { erp_state = "B23"; b23Count++; }
                                else //otherwise it is B1 or 0
                                    if ((_para.SEMdetect == false) || (sstate[(_pos / _pointsPerSecond)]))//if SEMs are disregarded or found
                                    { erp_state = "B1"; b1Count++; }
                                    else //if SEMs are regarded and not found
                                    { erp_state = "0"; a0Count++; }
                            }

                            else //it is one of the A-type segments: relative strength of Alpha power in the various lobes decides which type
                                if ((ocAl > paAl * _relThreshA1) && (ocAl > tmAl * _relThreshA1) && (ocAl > frAl * _relThreshA1))

                                { erp_state = "A1"; a1Count++; }

                                else // i.e. !((ocAl > paAl * a1factor) && (ocAl > tmAl * a1factor) && (ocAl > frAl * a1factor))
                                    if ((tmAl > frAl * _relThreshA2) || (paAl > frAl * _relThreshA2))

                                        if (_para.a2pa2t)
                                            if (tmAl > paAl)
                                                { erp_state = "A2T"; a2Tcount++; }
                                            else
                                                { erp_state = "A2P"; a2Pcount++; }
                                        else
                                            { erp_state = "A2"; a2Pcount++; }

                                    else { erp_state = "A3"; a3Count++; }
                        }

                        _resultsNode.AddMarker(-1, _pos + (time_after_ERP / 2), 1, "Comment", erp_state, true);

                        erp_output = erp_output + (mks[_j].Position / _pointsPerSecond) + "s " + erp_state + " "
                                   + ocAl + " " + ocDt + " 0 0 "
                                   + paAl + " " + paDt + " 0 0 "
                                   + tmAl + " " + tmDt + " 0 0 "
                                   + frAl + " " + frDt + " 0 0" + Environment.NewLine;

                        csv[resultscounter, 0] = (mks[_j].Position / _pointsPerSecond).ToString(CultureInfo.InvariantCulture);
                        csv[resultscounter, 1] = erp_state.ToString(CultureInfo.InvariantCulture);
                        csv[resultscounter, 2] = ocAl.ToString(CultureInfo.InvariantCulture);
                        csv[resultscounter, 3] = ocDt.ToString(CultureInfo.InvariantCulture);
                        csv[resultscounter, 4] = "0";
                        csv[resultscounter, 5] = "0";
                        csv[resultscounter, 6] = paAl.ToString(CultureInfo.InvariantCulture);
                        csv[resultscounter, 7] = paDt.ToString(CultureInfo.InvariantCulture);
                        csv[resultscounter, 8] = "0";
                        csv[resultscounter, 9] = "0";
                        csv[resultscounter, 10] = tmAl.ToString(CultureInfo.InvariantCulture);
                        csv[resultscounter, 11] = tmDt.ToString(CultureInfo.InvariantCulture);
                        csv[resultscounter, 12] = "0";
                        csv[resultscounter, 13] = "0";
                        csv[resultscounter, 14] = frAl.ToString(CultureInfo.InvariantCulture);
                        csv[resultscounter, 15] = frDt.ToString(CultureInfo.InvariantCulture);
                        csv[resultscounter, 16] = "0";
                        csv[resultscounter, 17] = "0";

                        resultscounter++;

                        _pb.Text = "ERP analysis: " + resultscounter;

                    }
                }
                if (erp_state == "") //if nothing was found
                {
                    erp_output = erp_output + "No valid Comment markers with the description '" + _para.erp_description + "' were found!";
                }
            }

            #region Finish

            if (!_para.leaveTempNodes)
            {
                _pb.Text = "Deleting temporary nodes";
                _pb.SetRange(0, 5);
                _pb.Position = 0;
                _alphaNode.Delete();
                _pb.StepIt();
                _deltathetaNode.Delete();
                _pb.StepIt();
                _afNode.Delete();
                _pb.StepIt();
                _bfNode.Delete();
                _pb.StepIt();
                if (_para.SEMdetect)
                    _seMfiltersNode.Delete();
                _hf.PurgeDeletedNodes();
                _pb.StepIt();
            }

            if (!_para.erp)
            {
                _pb.Text = "Adding markers";
                for (_i = 0; _i < _segs; _i++)
                    _resultsNode.AddMarker(-1, (_i * _pointsPerSecond), 1, "Comment", state[_i], true);
            }

            _pb.Text = "Writing output";

            //Output

            _out = "";

            for (_i = 0; _i < _segs; _i++)
                _out = _out + (_i + 1) + "    " + statelong[_i] + Environment.NewLine;

            var kanaele = aNode.Dataset.Channels[0].Name;
            for (_i = 1; _i < aNode.Dataset.Channels.Length; _i++)
                kanaele = kanaele + ", " + aNode.Dataset.Channels[(int)_i].Name;

            //lots of straightforward string operations to write the output file...

            _para.resultsFilename = _para.resultsFilenameWC.Replace("$h", aNode.HistoryFile.Name).Replace("$n", aNode.Name) + ".txt";

            _resultsNode.Description = "VIGALL Version: " + Versionstring + Environment.NewLine + "Output file name: " + _para.resultsFilename + Environment.NewLine;
            
            _resultsNode.Description = _resultsNode.Description + "Channels: " + kanaele + Environment.NewLine
                                        + "Absolute power threshold A: " + _absPowerThresholdA + Environment.NewLine
                                        + "Relative power threshold A: " + _relThreshA + Environment.NewLine
                                        + "Absolute power threshold B2/3: " + _absPowerThresholdB23 + Environment.NewLine
                                        + "Relative power threshold A1: " + _relThreshA1 + Environment.NewLine
                                        + "Relative power threshold A2: " + _relThreshA2 + Environment.NewLine
                                        + "Default length of C stages: " + _para.stageClength + " segments   Delta/Theta band starts from " + _para.lower_bound_DeltaTheta + " Hz" + Environment.NewLine + Environment.NewLine;


            _resultsNode.Description = _resultsNode.Description + acf_output;//should always be two lines: one line of excluded segments (can be very long), one line of result - or if ACF is not used, an empty line and one saying it was not used

            if (_para.ACFauto && _para.adaptAbsThresholds)
                _resultsNode.Description = _resultsNode.Description + "occROIalpha: " + ocAlMax + Environment.NewLine;
            else
                _resultsNode.Description = _resultsNode.Description + "No use of individual alpha frequency to adapt absolute current density thresholds." + Environment.NewLine;

            if (_para.SEMdetect)
                _resultsNode.Description = _resultsNode.Description + "SEM channel: " + _para.EOGname + " - Threshold: " + _para.SEMthreshold + " - Length: " + _para.SEMlength + " - Interval: " + _para.SEMbefore + " - Filter: " + _para.EOGfilterfrom + "-" + _para.EOGfilterto + " Hz" + Environment.NewLine + Environment.NewLine;
            else
                _resultsNode.Description = _resultsNode.Description + "No SEM detection" + Environment.NewLine + Environment.NewLine;

            if (_para.RRdetect)
                _resultsNode.Description = _resultsNode.Description + "Heart rate channel name: " + _para.ECGname + " - Minimum length: " + _para.minHeartrate + " ms - Heart rate Maximum length: " + _para.maxHeartrate + " ms" + Environment.NewLine;
            else
                _resultsNode.Description = _resultsNode.Description + "No Heart rate detection" + Environment.NewLine;

            if (_para.SCLEDAdetect)
                _resultsNode.Description = _resultsNode.Description + "Skin conductivity channel name: " + _para.SCLEDAname + Environment.NewLine;
            else
                _resultsNode.Description = _resultsNode.Description + "No skin conductivity calculation" + Environment.NewLine;

            if (_para.TEMPdetect)
                _resultsNode.Description = _resultsNode.Description + "Temperature channel name: " + _para.TEMPname + Environment.NewLine + Environment.NewLine;
            else
                _resultsNode.Description = _resultsNode.Description + "No temperature calculation" + Environment.NewLine + Environment.NewLine;

            if(_para.a2pa2t)
                _resultsNode.Description = _resultsNode.Description
                                        + "Number of segments classified 0:   " + a0Count + Environment.NewLine
                                        + "Number of segments classified A1:  " + a1Count + Environment.NewLine
                                        + "Number of segments classified A2P: " + a2Pcount + Environment.NewLine
                                        + "Number of segments classified A2T: " + a2Tcount + Environment.NewLine;
            else
                _resultsNode.Description = _resultsNode.Description + Environment.NewLine //extra line to make the length of the header independent of para.a2pa2t
                                        + "Number of segments classified 0:   " + a0Count + Environment.NewLine
                                        + "Number of segments classified A1:  " + a1Count + Environment.NewLine
                                        + "Number of segments classified A2:  " + a2Pcount + Environment.NewLine;

            _resultsNode.Description = _resultsNode.Description
                                        + "Number of segments classified A3:  " + a3Count + Environment.NewLine
                                        + "Number of segments classified B1:  " + b1Count + Environment.NewLine
                                        + "Number of segments classified B23: " + b23Count + Environment.NewLine
                                        + "Number of segments classified C:   " + ccount + Environment.NewLine
                                        + "Number of segments not classified: " + xcount + Environment.NewLine + Environment.NewLine;

            if (_para.erp)
                _resultsNode.Description = _resultsNode.Description + erp_output + Environment.NewLine + Environment.NewLine;
            else
            {
                _resultsNode.Description = _resultsNode.Description + "Power values" + Environment.NewLine
                                            + _out;
            }

            using (var outfile = new StreamWriter(_para.resultsPath + _para.resultsFilename))
            {
                outfile.Write(_resultsNode.Description);
            }

            if (_para.csv)
            {
                var csvOutput = "";
                if (!_para.erp)
                {
                    for (_i = 0; _i < _segs; _i++)
                        if (csv[_i, 1]!="X")
                            csvOutput = csvOutput + csv[_i, 0] + ";" + csv[_i, 1] + ";" + csv[_i, 2] + ";" + csv[_i, 3] + ";" + csv[_i, 4] + ";" + csv[_i, 5] + ";" + csv[_i, 6] + ";" + csv[_i, 7] + ";" + csv[_i, 8] + ";" + csv[_i, 9] + ";" + csv[_i, 10] + ";" + csv[_i, 11] + ";" + csv[_i, 12] + ";" + csv[_i, 13] + ";" + csv[_i, 14] + ";" + csv[_i, 15] + ";" + csv[_i, 16] + ";" + csv[_i, 17] + ";" + csv[_i, 18] + ";" + csv[_i, 19] + ";" + csv[_i, 20] + Environment.NewLine;
                        else
                            csvOutput = csvOutput + csv[_i, 0] + ";" + csv[_i, 1] + ";" + csv[_i, 2] + ";" + csv[_i, 3] + ";" + csv[_i, 4] + Environment.NewLine;
                }
                else //para.erp
                {
                    for (_i = 0; _i < _segs; _i++)
                        if (csv[_i, 1] != null) //the number of erp results will typically be a lot lower than segs
                            csvOutput = csvOutput + csv[_i, 0] + ";" + csv[_i, 1] + ";" + csv[_i, 2] + ";" + csv[_i, 3] + ";" + csv[_i, 4] + ";" + csv[_i, 5] + ";" + csv[_i, 6] + ";" + csv[_i, 7] + ";" + csv[_i, 8] + ";" + csv[_i, 9] + ";" + csv[_i, 10] + ";" + csv[_i, 11] + ";" + csv[_i, 12] + ";" + csv[_i, 13] + ";" + csv[_i, 14] + ";" + csv[_i, 15] + ";" + csv[_i, 16] + ";" + csv[_i, 17] + Environment.NewLine;
                        else
                            break;
                }

                using (var outfile = new StreamWriter(_para.resultsPath + _para.resultsFilename.Replace(".txt", ".csv")))
                {
                    outfile.Write(csvOutput);
                }
            }

            // Call Finish() to complete the new history file.
            _resultsNode.Finish(new Guid(VigallId));
            return "";

            #endregion //Finish
            #endregion //Actual VIGALL Operation
        }


        private string create_and_initialize_main_temp_nodes(IHistoryNode aNode)
        {
            double dtBandstart = _para.lower_bound_DeltaTheta; //start of DeltaTheta
            double dtBandend = 7; //end of DeltaTheta
            double aBandstart = 8; //start of Alpha
            double aBandend = 12; //end of Alpha

            if (_para.adapted_bands)
            {
                double stretchfactor = _para.alphacenter / 10; //i.e. <1 if alpha center frequency is found to be <10 etc.
                dtBandstart = Math.Round(dtBandstart * stretchfactor, 2); //both bands are stretched or squished accordingly
                dtBandend = Math.Round(dtBandend * stretchfactor, 2);
                aBandstart = Math.Round(aBandstart * stretchfactor, 2);
                aBandend = Math.Round(aBandend * stretchfactor, 2);
            }
            else //old way of placing Alpha band start and end
            {
                aBandstart = _para.alphacenter - 2; // alpha is individual band fixed width, DT is always fixed to 2-7
                aBandend = _para.alphacenter + 2;
            }

            var loretaParameters = "<LORETA> " +
                                       "<KeepOldChannels>false</KeepOldChannels>" +
                                       "<NewChannelsOnTop>true</NewChannelsOnTop>" +
                                       "<Anatomy>MNI-Average305-T1</Anatomy>" +
                                       "<ROIDefinitions>" +
                                       "<ROI>" +
                                           "<Name>frontal</Name> " +
                                           "<VoxelCalculationMethod>4</VoxelCalculationMethod>" +
                                           "<Lobe>Frontal Lobe LR</Lobe>" +
                                       "</ROI>" +
                                       "<ROI>" +
                                           "<Name>parietal</Name>" +
                                           "<VoxelCalculationMethod>4</VoxelCalculationMethod>" +
                                           "<Lobe>Parietal Lobe LR</Lobe>" +
                                       "</ROI>" +
                                       "<ROI>" +
                                           "<Name>occipital</Name>" +
                                           "<VoxelCalculationMethod>4</VoxelCalculationMethod>" +
                                           "<Lobe>Occipital Lobe LR</Lobe>" +
                                       "</ROI>" +
                                       "<ROI>" +
                                           "<Name>temporal</Name>" +
                                           "<VoxelCalculationMethod>4</VoxelCalculationMethod>" +
                                           "<Lobe>Temporal Lobe LR</Lobe>" +
                                       "</ROI>";

            loretaParameters = loretaParameters + "</ROIDefinitions> </LORETA>";

            _pb.Text = "Preprocessing: Filtering Alpha: " + aBandstart + "-" + aBandend + " Hz";
            _pb.StepIt();
            _analyzer.Transformation.Do("Filters", "<Filters><LowCutoff>" + aBandstart + ",48</LowCutoff><HighCutoff>" + aBandend + ",48</HighCutoff></Filters>", aNode, "VIGALL Alpha");
            //Filters have to be called in this roundabout fashion. 

            //Now we need to find the node that results from this.
            if (_hf.FindNodes("VIGALL Alpha").Length < 1)
                return "Cannot find Alpha node that should have been created!" + _badFail;
            //else
            _afNode = _hf.FindNodes("VIGALL Alpha")[0];

            _pb.Text = "Preprocessing: Filtering Delta/Theta: " + dtBandstart + "-" + dtBandend + " Hz";
            _pb.StepIt();
            _analyzer.Transformation.Do("Filters", "<Filters><LowCutoff>" + dtBandstart + ",48</LowCutoff><HighCutoff>" + dtBandend + ",48</HighCutoff></Filters>", aNode, "VIGALL DeltaTheta");
            //Filters have to be called in this roundabout fashion. 

            //Now we need to find the node that results from this.
            if (_hf.FindNodes("VIGALL DeltaTheta").Length < 1)
                return "Cannot find DeltaTheta node that should have been created!" + _badFail;
            //else
            _bfNode = _hf.FindNodes("VIGALL DeltaTheta")[0];

                _pb.Text = "Preprocessing: LORETA Alpha";
                _pb.StepIt();
                _analyzer.Transformation.Do("LORETATransformation", loretaParameters, _afNode, "VIGALL LORETA Alpha");
                _pb.Title = _titlestring;

                if (_hf.FindNodes("VIGALL LORETA Alpha").Length < 1)
                {
                    if (_para.batch) //if we're batch processing, this is assumed to be a "normal" lack of memory problem
                        return "Limit of LORETA operations per batch processing reached.";
                    //if not, something truly odd has happened
                    return "Cannot find LORETA Alpha node that should have been created!" + _badFail;
                }
                //else
                _alphaNode = _hf.FindNodes("VIGALL LORETA Alpha")[0];

                _pb.Text = "Preprocessing: LORETA Delta/Theta";
                _pb.StepIt();
                _analyzer.Transformation.Do("LORETATransformation", loretaParameters, _bfNode, "VIGALL LORETA DeltaTheta");
                _pb.Title = _titlestring;

                if (_hf.FindNodes("VIGALL LORETA DeltaTheta").Length < 1)
                    return "Cannot find LORETA Delta/Theta node that should have been created!" + _badFail;
                //else
                _deltathetaNode = _hf.FindNodes("VIGALL LORETA DeltaTheta")[0];

            if ((_alphaNode.Dataset.Length != aNode.Dataset.Length) || (_alphaNode.Dataset.Length != _deltathetaNode.Dataset.Length))
            //this should never happen, but we check it just to be sure
                return "Nodes dataset lengths not found to be equal.";
            

            if ((Math.Abs(_alphaNode.Dataset.SamplingInterval - aNode.Dataset.SamplingInterval) > 0) || (Math.Abs(_alphaNode.Dataset.SamplingInterval - _deltathetaNode.Dataset.SamplingInterval) > 0) )
            //this should never happen, but we check it just to be sure
                return "Nodes dataset sampling intervals not found to be equal.";

            return "";//success! no errors! yay!
        }

        private string DetectSem(IHistoryNode aNode) //this identifies slow eye movements (SEM) and places markers that will be the basis for the "S" states
        {
            _pb.Text = "Preprocessing: Slow eye movements filtering" + _para.EOGfilterfrom + " to " + _para.EOGfilterto + " Hz";

            if (aNode.Dataset.Channels[_para.EOGname] == null) //no EOG channel found
                return "EOG data channel '" + _para.EOGname + "'not found in active node.";

            _analyzer.Transformation.Do("Filters", "<Filters><LowCutoff>" + _para.EOGfilterfrom + ",48</LowCutoff><HighCutoff>" + _para.EOGfilterto + ",48</HighCutoff><Notch>50,48</Notch></Filters>", aNode, "VIGALL SEM Filters");
            //SEM filters have to be called in this roundabout fashion. The node that results from it is found next.

            if (_hf.FindNodes("VIGALL SEM Filters").Length < 1)
                return "Cannot find SEM Filters node that should have been created!"+_badFail;
            //else...
            _seMfiltersNode = _hf.FindNodes("VIGALL SEM Filters")[0];

            int eognr; //get number so we can call SEMfiltersNode.Dataset.GetData()
            for (eognr = 0; eognr < _seMfiltersNode.Dataset.Channels.Length; eognr++)
                if (_seMfiltersNode.Dataset.Channels[eognr].Name == _para.EOGname) break;


            var eogArray = _seMfiltersNode.Dataset.GetData(0, _t, new[] { eognr });
            if (eogArray == null)
                return "EOG data channel not found in SEM Filters node.";

            _pb.Text = "Preprocessing: Slow eye movements detection";

            uint semStart;
            semStart = 0;
            uint semStop;
            semStop = 0;
            int min;
            min = 0;
            int max;
            max = 0;
            uint length; //translate from microseconds to number of data points
            length = _para.SEMlength / 1000 * _pointsPerSecond;
            uint before;
            before = _para.SEMbefore / 1000 * _pointsPerSecond;
            var threshold = _para.SEMthreshold;

            for (_u = 0; _u < _t; _u++)
            {
                if (eogArray[_u] <= eogArray[min])
                {
                    min = _u;
                }
                if (eogArray[_u] >= eogArray[max])
                {
                    max = _u;
                }

                if ((min + length) <= _u) //if min "falls out of the window"
                {
                    min++; //find new minimum within the length
                    for (var m = min; m < _u; m++)
                    {
                        if (eogArray[m] <= eogArray[min])
                            min = m;
                    }
                }
                if ((max + length) <= _u) //if max "falls out of the window"
                {
                    max++; //find new maximum within the length
                    for (var m = max; m < _u; m++)
                    {
                        if (eogArray[m] >= eogArray[max])
                            max = m;
                    }
                }


                
                if (eogArray[min] + threshold <= eogArray[max]) //true -> SEM
                {
                    if (semStop < _u) //if we're inside an undetected SEM
                    {
                        semStart = (uint)_u - before; //...we detect it now. It is assumed to have begun some time before (default 6 seconds)...
                        if (semStart > _u + before) semStart = 0; //...but no earlier than the beginning of the dataset.
                    }
                    semStop = (uint)_u + before; //Whether the SEM is new or now, it is assumed to end no earlier than some time (default 6 seconds) later...
                    if (semStop > _t - 1) semStop = _t - 1; //...but no later than the end of the dataset.
                }
                
                if((eogArray[min] + threshold > eogArray[max])||(semStop == _t - 1)) //not an SEM or end of the dataset
                    if ((semStop>0)&&(semStop == _u)) //If the most recent SEM is past but not marked yet...
                    {
                        _resultsNode.AddMarker(eognr, semStart, semStop - semStart, "Comment", "Slow Eye Movement");
                        semStop = 0;                                                              
                    }
            }
        return ""; //success!
        }

        public void MyDft(int i, int dft_from, int dft_to)//a normal discrete Fourier transform, with adaptations to the present data structures
        {
            var arg = -6.28318530717959/_pointsPerFt; //= -2.0 * Math.PI / _pointsPerFT;
            for (var n = dft_from; n < dft_to; n++)
            {
                Complex ergebnis1 = new Complex();
                Complex ergebnis2 = new Complex();
                for (var k = 0; k < _pointsPerFt; k++)
                {
                    ergebnis1 += new Complex(_o1[(i * _pointsPerIncrement) + k], 0) * Complex.FromPolarCoordinates(1, arg * n * k);
                    ergebnis2 += new Complex(_o2[(i * _pointsPerIncrement) + k], 0) * Complex.FromPolarCoordinates(1, arg * n * k);
                }
                _output[i, n] = (float)(((ergebnis1 * ergebnis1).Magnitude + (ergebnis2 * ergebnis2).Magnitude) / 2);
            }
        }

        private bool CheckHistoryNode(IHistoryNode checkNode)
        {
            // Do some checks.
            if (checkNode.Dataset.Type == VisionDataType.FrequencyDomainComplex
                || checkNode.Dataset.Type == VisionDataType.TimeDomainComplex
                || checkNode.Dataset.Type == VisionDataType.TimeFrequencyDomain
                || checkNode.Dataset.Type == VisionDataType.TimeFrequencyDomainComplex)
            {
                MessageDisplay.ShowMessage("VIGALL::Execute", _titlestring, "Sorry, only non-complex, non-layered data.");
                return false;
            }

            return true;
        }
        
        #region AddIn context initializaion
        /// <summary>
        /// Initializes the AddIn within its context. Important only for advanced AddIn types.
        /// </summary>
        /// <param name="context">Context in which the AddIn is visible (ribbon, context menu...)</param>
        /// <returns>False if AddIn menu entry should be disabled</returns>
        public bool InitContext(IAnalyzerContext context)
        {
            return true;
        } 

        #endregion

        // Creates a secondary history file to write output into.
        private INewHistoryNode CreateOutputFile(IHistoryNode aNode)
        {
            try
            {
                
                // Remove output file if it exists.
                var sFileName = "VIGALL";
                var i = 2;
                while (aNode[sFileName] != null)
                {
                    sFileName = "VIGALL" + i;
                    i++;
                }

                // Build new History Node based on properties of active node, because the only thing that is going to be different in the new node is markers.
                var newNode = _analyzer.CreateNode(sFileName, aNode, true);
                if (newNode == null)
                {
                    MessageDisplay.ShowMessage("VIGALL::Execute", _titlestring, "Unable to create new node. Exiting.");
                    return null;
                }

                return newNode;
            }
            catch
            {
                // exit the AddIn, Message will be shown
                return null;
            }
        }

        #region Private Variables

        string _h;

        IApplication _analyzer; //the application interface
        Parameters _para; //see Parameters.cs
        IHistoryFile _hf; // History file that includes ActiveNode

        IHistoryNode _afNode; //nodes created by filtering
        IHistoryNode _bfNode;
        IHistoryNode _alphaNode; //nodes created by LORETA after filtering
        IHistoryNode _deltathetaNode;
        IHistoryNode _seMfiltersNode;
        INewHistoryNode _resultsNode;

        IHistoryNode[] _activeNodes;


        IProgressBar _pb;

        int _u;
        int _j;
        uint _i;
        uint _pos;

        uint _t; //this is "points till end of last segment" or "all points except the last few of them that don't make a full segment and are therefore disregarded"
        uint _segs; //number of segments
        uint _pointsPerSecond;

        double _absPowerThresholdA;
        double _relThreshA;
        double _relThreshA1;
        double _relThreshA2;
        double _absPowerThresholdB23;

        //these are variables we use to have doEverything and myDFT talk to each other
        uint _pointsPerFt;
        uint _pointsPerIncrement;
        uint _numberOfFt;
        float[] _o1;
        float[] _o2;
        float[,] _output;

        readonly string _badFail = Environment.NewLine+"This should only happen if your software is not up to date or you lack permission to edit data files.";
        private const string Versionstring = "VIGALL 2.1";
        string _titlestring;
        string _out; //the output line to be written

        #endregion
    }
}