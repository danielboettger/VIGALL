/*----------------------------------------------------------------------------*/
//  Project:	Vigilance
/*----------------------------------------------------------------------------*/
//  Name:		FrmParameterDlg.cs
//  Purpose:	AddIn that calculates vigilance states -- Parameter dialog.
//  Copyright:	Copyright © University of Leipzig 2010-2018
//  Date:		2018-07-12
//  Version:	2.1
/*----------------------------------------------------------------------------*/

using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using BrainVision.Interfaces;
using BrainVision.Support;
using BrainVision.AnalyzerAutomation;

namespace VIGALL
{
    // CODING: Add any UI you need for your project to this form. 
    // CODING: The example form does not keep a copy of the Parameters instance passed to it. Parameter state is
    // stored completely in the UI controls, and is read again when the dialog closes. This has some advantages, 
    // because changes made in stored parameters would change the Parameters instance originally passed (reference 
    // type). The example dialog does not change the Parameters instance in AddIn.Execute() until it is explicitly 
    // read back. Also, keeping parameters in a stored Parameters instance and the UI would cause ambiguity.
    // CODING: The example dialog validates UI control input as part of the parameter reading process, and not
    // through the Control.Validating event family provided by .NET. The user can edit the parameters freely, and
    // they are validated only when he clicks the Ok button. This behaviour is preferred in Analyzer 2
    // because cancelling a Control.Validating event intentionally keeps the user's input focus locked inside 
    // the control -- even when he is just trying to click the Cancel button.

    /// <summary>
    /// Form for presenting a parameter dialog.
    /// </summary>
    public partial class FrmParameterDlg : Form
    {
        public FrmParameterDlg()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Parameters currently represented of the form. Set parameters to change state of the Form's UI
        /// controls. Get parameters to read current UI control settings.
        /// </summary>
        internal Parameters Parameters
        {
            get
            {
                Parameters p = new Parameters();
                ReadParameters(p);
                return p;
            }
            set
            {
                int histnodescount = 0;
                for (int u = 0; u < AutomationSupport.Application.HistoryFiles.Length; u++)
                    if (AutomationSupport.Application.HistoryFiles[u].IsOpen)
                        histnodescount++;
                checkBoxBatch.Text = "Process all history nodes named " + AutomationSupport.Application.ActiveNode.Name + " from " + histnodescount.ToString() + " history files";
                if (histnodescount > 1)
                {
                    checkBoxBatch.Enabled = true;
                    checkBoxBatchExclude.Enabled = true;
                }
                ECGnameInput.Text = value.ECGname;
                EOGnameInput.Text = value.EOGname;
                SCLEDAnameInput.Text = value.SCLEDAname;
                TEMPnameInput.Text = value.TEMPname;
                threshold1Input.Text = value.absThresholdA.ToString();
                threshold2Input.Text = value.relThresholdA.ToString();
                threshold3Input.Text = value.relThresholdA1.ToString();
                threshold4Input.Text = value.relThresholdA2.ToString();
                b23Input.Text = value.absThresholdB23.ToString();
                minHeartrateInput.Text = value.minHeartrate.ToString();
                maxHeartrateInput.Text = value.maxHeartrate.ToString();
                checkBox2.Checked = value.SEMdetect;
                checkBox3.Checked = value.RRdetect;
                checkBox5.Checked = value.SCLEDAdetect;
                checkBox6.Checked = value.TEMPdetect;
                stageClengthInput.Text = value.stageClength.ToString();
                SEMlengthInput.Text = value.SEMlength.ToString();
                SEMthresholdInput.Text = value.SEMthreshold.ToString();
                SEMbeforeInput.Text = value.SEMbefore.ToString();
//                SEMafterInput.Text = value.SEMafter.ToString();
                segmentLengthInput.Text = value.segmentLength.ToString();
                resultsPathInput.Text = value.resultsPath;
                resultsFilenameInput.Text = value.resultsFilenameWC;

                checkBoxROI.Checked = value.Amygdala_LORETA;
                checkBoxNewBands.Checked = value.adapted_bands;
                checkBoxNewTree.Checked = value.new_tree;
                checkBoxA2PA2T.Checked = value.a2pa2t;
                checkBoxAPFauto.Checked = value.ACFauto;
                alphaStartInput.Enabled = checkBoxAPFauto.Checked;
                alphaStartInput.Text = value.ACFfrom.ToString();
                alphaEndInput.Enabled = checkBoxAPFauto.Checked;
                alphaEndInput.Text = ((uint)(AutomationSupport.Application.ActiveNode.Dataset.Length / (1000000 / AutomationSupport.Application.ActiveNode.Dataset.SamplingInterval))).ToString();
                checkBoxKriterienAnpassen.Checked = value.adaptAbsThresholds;
                kriterienFaktorInput.Text = value.absThresholdsFactor.ToString();

                checkBoxKriterienAnpassen.Enabled = checkBoxAPFauto.Checked;
                kriterienFaktorInput.Enabled = checkBoxAPFauto.Checked;
                bandBox9.Enabled = !checkBoxNewBands.Checked;
                threshold1Input.Enabled = !checkBoxKriterienAnpassen.Checked;
                b23Input.Enabled = !checkBoxKriterienAnpassen.Checked;

                checkBoxERP.Checked = value.erp;
                inputERPlabel.Text = value.erp_description;
                inputERPbefore.Text = value.erp_before.ToString();
                inputERPafter.Text = value.erp_after.ToString();

                checkBoxBatch.Checked = value.batch;
                checkBoxBatchExclude.Checked = value.batchexclude;
                checkBox7.Checked = value.leaveTempNodes;
                checkBox8.Checked = value.purgeOldTempNodes;

                textBox_UntergrenzeDT.Text = value.lower_bound_DeltaTheta.ToString();
                
                alphacenterfrequency = value.alphacenter;
                ar = value.alpharadius;
                bandBox9.Text = ar.ToString();
                alphapeakInput.Text = alphacenterfrequency.ToString();
                updateBaender();

                //main parameters
                tooltips.SetToolTip(resultsFilenameInput, "Enter the name of the .txt file that will contain the results.\n$h - History File Name\n$n - Active Node Name");
                tooltips.SetToolTip(checkBox2, "Check if VIGALL should detect slow eye movements.\nSEM detection is necessary for the distinction between B1 and 0 states.\nThis requires an EOG or HEOG channel.");
                tooltips.SetToolTip(checkBoxCSV, "Check to have VIGALL create a comma separated values results file\nin addition to the plaintext results file.");
                tooltips.SetToolTip(EOGnameInput, "Enter the name of the channel that contains HEOG (recommended) or EOG information.");
                tooltips.SetToolTip(SEMthresholdInput, "Minimum power differential threshold for SEM detection.\nRecommended values: 200 µV for HEOG, 150µV for EOG.");
                tooltips.SetToolTip(checkBoxAPFauto, "Check if VIGALL should automatically detect the subject's alpha center frequency.");
                tooltips.SetToolTip(alphapeakInput, "The subject's alpha center frequency, used to personalize the subject's alpha power band.");
                tooltips.SetToolTip(stageClengthInput, "Default length of stage C interval after sleep spindle or K-complex markers.");
                tooltips.SetToolTip(checkBoxKriterienAnpassen, "Check if VIGALL should automatically adapt absolute power thresholds to the subject's individual power distribution.");
                tooltips.SetToolTip(threshold1Input, "Minimum Alpha band LORETA power for stage A classification.");
                tooltips.SetToolTip(b23Input, "Minimum Delta/Theta band LORETA power for stage B2/3 classification.");
                tooltips.SetToolTip(resultsPathInput, "Enter the name of the directory where the results file will be stored.");

                //advanced

                tooltips.SetToolTip(checkBox7, "Check to keep the temporary nodes created by VIGALL.\nThis is NOT recommended for batch processing.");
                tooltips.SetToolTip(bandBox9, "Enter the maximum distance from the alpha center frequency for signals to include in power value calculations.");
                tooltips.SetToolTip(checkBox5, "Check if VIGALL should display average skin conductance values for classified segments.");
                tooltips.SetToolTip(checkBox6, "Check if VIGALL should display average temperature values for classified segments.");
                tooltips.SetToolTip(checkBox3, "Check if VIGALL should display estimated heartbeat intervals for classified segments.");
                tooltips.SetToolTip(SCLEDAnameInput, "Enter the name of the channel that contains skin conductance measurements.");
                tooltips.SetToolTip(TEMPnameInput, "Enter the name of the channel that contains temperature measurements.");
                tooltips.SetToolTip(ECGnameInput, "Enter the name of the channel that contains electrocardiogram measurements.");
                tooltips.SetToolTip(minHeartrateInput, "Enter the minimal heart rate. Segments with estimated heartbeat intervals lower than this will not be classified.\nRecommended value: 300ms");
                tooltips.SetToolTip(maxHeartrateInput, "Enter the maximal heart rate. Segments with estimated heartbeat intervals higher than this will not be classified.\nRecommended value: 1500ms");
                tooltips.SetToolTip(SEMlengthInput, "Enter the minimum length of slow eye movements to detect.");
                tooltips.SetToolTip(SEMbeforeInput, "Enter the interval before and after a SEM power differential to be influenced by positive SEM detection.");
                tooltips.SetToolTip(threshold2Input, "This is the minimum factor by which Alpha power exceeds Delta/Theta power in A states.\nRecommended value: 2");
                tooltips.SetToolTip(threshold3Input, "This is the minimum factor by which occipital Alpha power exceeds parietal, temporal or frontal Alpha power in A1 states.\nRecommended value: 2");
                tooltips.SetToolTip(threshold4Input, "This is the minimum factor by which frontal Alpha power exceeds parietal or temporal Alpha power in A2 states.\nRecommended value: 4");
                tooltips.SetToolTip(alphaStartInput, "Enter the start point of the range within which to detect the alpha center frequency.\nIf this is outside the dataset (i.e. if you enter 300 in a 200 seconds measurement), alpha center detection will fail.\nRecommended value: 0 seconds (start of the dataset)");
                tooltips.SetToolTip(alphaEndInput, "Enter the end point of the range within which to detect the alpha center frequency.\nThe end of the search range is also constrained by the actual length of the dataset.\nRecommended value: length of the dataset or more");

                //erp
                tooltips.SetToolTip(inputERPlabel, "Description that distinguishes markers of events where classification is to be performed. Marker type (Stimulus, Comment etc.) is disregarded.");
                tooltips.SetToolTip(inputERPbefore, "Milliseconds before each of the specified markers that are to be included in classification.");
                tooltips.SetToolTip(inputERPafter, "Milliseconds after each of the specified markers that are to be included in classification.");

                //buttons
                tooltips.SetToolTip(resetButton, "This button returns all parameters to their default values.");
                tooltips.SetToolTip(buttonOK, "Press this button to begin classification.");
                tooltips.SetToolTip(buttonCancel, "This button ends the Add In.");
                //tooltips.SetToolTip(, ".");
            }
        }

        /// <summary>
        /// Reads parameters from UI elements.
        /// </summary>
        /// <param name="p">Parameters instance to read control values into</param>
        /// <returns>True if no validation problems</returns>
        private bool ReadParameters(Parameters p)
        {
            errorProvider.SetError(threshold1Input, "");
            errorProvider.SetError(threshold2Input, "");
            errorProvider.SetError(threshold3Input, "");
            errorProvider.SetError(threshold4Input, "");
            errorProvider.SetError(b23Input, "");

            errorProvider.SetError(ECGnameInput, "");
            errorProvider.SetError(minHeartrateInput, "");
            errorProvider.SetError(maxHeartrateInput, "");

            errorProvider.SetError(EOGnameInput, "");
            errorProvider.SetError(SEMlengthInput, "");
            errorProvider.SetError(SEMthresholdInput, "");
            errorProvider.SetError(SEMbeforeInput, "");

            errorProvider.SetError(inputERPlabel, "");
            errorProvider.SetError(inputERPbefore, "");
            errorProvider.SetError(inputERPafter, "");

            errorProvider.SetError(alphapeakInput, "");
            errorProvider.SetError(alphaStartInput, "");
            errorProvider.SetError(alphaEndInput, "");

            errorProvider.SetError(stageClengthInput, "");
            errorProvider.SetError(resultsFilenameInput, "");
            errorProvider.SetError(resultsPathInput, "");

            errorProvider.SetError(kriterienFaktorInput, "");
            errorProvider.SetError(segmentLengthInput, "");

            errorProvider.SetError(textBox_UntergrenzeDT, "");

            // Validate & collect values

            //checkboxes to bool values is easy
            p.leaveTempNodes = checkBox7.Checked;
            p.purgeOldTempNodes = checkBox8.Checked;
            p.Amygdala_LORETA = checkBoxROI.Checked;
            p.adapted_bands = checkBoxNewBands.Checked;
            p.new_tree = checkBoxNewTree.Checked;
            p.a2pa2t = checkBoxA2PA2T.Checked;
            p.ACFauto = checkBoxAPFauto.Checked;
            p.adaptAbsThresholds = checkBoxKriterienAnpassen.Checked;
            p.batch = checkBoxBatch.Checked;
            p.batchexclude = checkBoxBatchExclude.Checked;
            p.csv = checkBoxCSV.Checked;

            if ((checkBox2.Checked)&&(string.IsNullOrEmpty(EOGnameInput.Text)))
            {
                errorProvider.SetError(EOGnameInput, "Please provide a name.");
                return false;
            }

            if ((checkBox2.Checked)&&(AutomationSupport.Application.ActiveNode.Dataset.Channels[EOGnameInput.Text] == null))
            {
                errorProvider.SetError(EOGnameInput, "No such channel found in history node.");
                return false;
            }

            p.EOGname = EOGnameInput.Text;

            if ((checkBox5.Checked) && (string.IsNullOrEmpty(SCLEDAnameInput.Text)))
            {
                errorProvider.SetError(SCLEDAnameInput, "Please provide a name.");
                return false;
            }

            if ((checkBox5.Checked) && (AutomationSupport.Application.ActiveNode.Dataset.Channels[SCLEDAnameInput.Text] == null))
            {
                errorProvider.SetError(SCLEDAnameInput, "No such channel found in history node.");
                return false;
            }

            p.SCLEDAdetect = checkBox5.Checked;
            p.SCLEDAname = SCLEDAnameInput.Text;

            if ((checkBox6.Checked) && (string.IsNullOrEmpty(TEMPnameInput.Text)))
            {
                errorProvider.SetError(TEMPnameInput, "Please provide a name.");
                return false;
            }

            if ((checkBox6.Checked) && (AutomationSupport.Application.ActiveNode.Dataset.Channels[TEMPnameInput.Text] == null))
            {
                errorProvider.SetError(TEMPnameInput, "No such channel found in history node.");
                return false;
            }

            p.TEMPdetect = checkBox6.Checked;
            p.TEMPname = TEMPnameInput.Text;

            if (!double.TryParse(threshold1Input.Text, out p.absThresholdA))
            {
                errorProvider.SetError(threshold1Input, "Needs to be numeric.");
                return false;
            }

            if (!double.TryParse(threshold2Input.Text, out p.relThresholdA))
            {
                errorProvider.SetError(threshold2Input, "Needs to be numeric.");
                return false;
            }

            if (!double.TryParse(threshold3Input.Text, out p.relThresholdA1))
            {
                errorProvider.SetError(threshold3Input, "Needs to be numeric.");
                return false;
            }

            if (!double.TryParse(threshold4Input.Text, out p.relThresholdA2))
            {
                errorProvider.SetError(threshold4Input, "Needs to be numeric.");
                return false;
            }

            if (!double.TryParse(b23Input.Text, out p.absThresholdB23))
            {
                errorProvider.SetError(b23Input, "Needs to be numeric.");
                return false;
            }

            if (!float.TryParse(alphapeakInput.Text, out p.alphacenter))
            {
                errorProvider.SetError(alphapeakInput, "Needs to be numeric.");
                return false;
            }

            if (!float.TryParse(bandBox9.Text, out p.alpharadius))
            {
                errorProvider.SetError(bandBox9, "Needs to be numeric.");
                return false;
            }


            if (!float.TryParse(kriterienFaktorInput.Text, out p.absThresholdsFactor))
            {
                errorProvider.SetError(kriterienFaktorInput, "Needs to be numeric.");
                return false;
            }


            if ((p.alphacenter < 8) || (p.alphacenter > 13))
            {
                errorProvider.SetError(alphapeakInput, "Alpha peak should be between 8 and 13 Hz.");
                return false;
            }

            if (!uint.TryParse(stageClengthInput.Text, out p.stageClength))
            {
                errorProvider.SetError(stageClengthInput, "Needs to be a positive integer.");
                return false;
            }

            if ((checkBoxAPFauto.Checked) && (!uint.TryParse(alphaStartInput.Text, out p.ACFfrom)))
            {
                errorProvider.SetError(alphaStartInput, "Needs to be a positive integer.");
                return false;
            }

            if ((checkBoxAPFauto.Checked) && (!uint.TryParse(alphaEndInput.Text, out p.ACFto)))
            {
                errorProvider.SetError(alphaEndInput, "Needs to be a positive integer.");
                return false;
            }

            if (p.ACFfrom + 10 > p.ACFto)//this only happens if the previous 2 tryparse()s succeeded
            {
                errorProvider.SetError(alphaStartInput, "Start and end point need to be at least 10 seconds apart.");
                errorProvider.SetError(alphaEndInput, "Start and end point need to be at least 10 seconds apart.");
                return false;
            }

            if (!float.TryParse(textBox_UntergrenzeDT.Text, out p.lower_bound_DeltaTheta))
            {
                errorProvider.SetError(textBox_UntergrenzeDT, "Needs to be numeric.");
                return false;
            }

            if ((p.lower_bound_DeltaTheta != 0.5) && (p.lower_bound_DeltaTheta != 1) && (p.lower_bound_DeltaTheta != 1.5) && (p.lower_bound_DeltaTheta != 2) && (p.lower_bound_DeltaTheta != 2.5) && (p.lower_bound_DeltaTheta != 3) && (p.lower_bound_DeltaTheta != 3.5) && (p.lower_bound_DeltaTheta != 4) && (p.lower_bound_DeltaTheta != 4.5))
            {
                errorProvider.SetError(textBox_UntergrenzeDT, "Needs to be a multiple of 0.5 Hz, minimum 0.5, maximum 4.5 Hz.");
                return false;
            }


            p.createSegments = checkBox1.Checked;

            if ((checkBox1.Checked)&&(!uint.TryParse(segmentLengthInput.Text, out p.segmentLength)))
            {
                errorProvider.SetError(segmentLengthInput, "Needs to be a positive integer.");
                return false;
            }

            if (p.segmentLength < 100)
            {
                errorProvider.SetError(segmentLengthInput, "Needs to be at least 100 ms. Values between 500 and 3000 are recommended.");
                return false;
            }

            if ((checkBox3.Checked) && (string.IsNullOrEmpty(ECGnameInput.Text)))
            {
                errorProvider.SetError(ECGnameInput, "Please provide a name.");
                return false;
            }

            if ((checkBox3.Checked) && (AutomationSupport.Application.ActiveNode.Dataset.Channels[ECGnameInput.Text] == null))
            {
                errorProvider.SetError(ECGnameInput, "No such channel found in history node.");
                return false;
            }

            p.RRdetect = checkBox3.Checked;
            p.ECGname = ECGnameInput.Text;


            if ((checkBox3.Checked)&&(!uint.TryParse(minHeartrateInput.Text, out p.minHeartrate)))
            {
                errorProvider.SetError(segmentLengthInput, "Needs to be a positive integer.");
                return false;
            }

            if ((checkBox3.Checked)&&(!uint.TryParse(maxHeartrateInput.Text, out p.maxHeartrate)))
            {
                errorProvider.SetError(maxHeartrateInput, "Needs to be a positive integer.");
                return false;
            }

            p.SEMdetect = checkBox2.Checked;

            if (!uint.TryParse(SEMbeforeInput.Text, out p.SEMbefore))
            {
                errorProvider.SetError(SEMbeforeInput, "Needs to be a positive integer.");
                return false;
            }

            if (p.SEMbefore % 10 != 0)
            {
                errorProvider.SetError(SEMbeforeInput, "Needs to be divisible by 10.");
                return false;
            }

/*            if (!uint.TryParse(SEMafterInput.Text, out p.SEMafter))
            {
                errorProvider.SetError(SEMafterInput, "Needs to be a positive integer.");
                return false;
            }

            if (p.SEMafter % 10 != 0)
            {
                errorProvider.SetError(SEMafterInput, "Needs to be divisible by 10.");
                return false;
            }*/

            if (!uint.TryParse(SEMlengthInput.Text, out p.SEMlength))
            {
                errorProvider.SetError(SEMlengthInput, "Needs to be a positive integer.");
                return false;
            }

            if (p.SEMlength % 10 != 0)
            {
                errorProvider.SetError(SEMlengthInput, "Needs to be divisible by 10.");
                return false;
            }

            if (!uint.TryParse(SEMthresholdInput.Text, out p.SEMthreshold))
            {
                errorProvider.SetError(SEMthresholdInput, "Needs to be a positive integer.");
                return false;
            }

            p.erp = checkBoxERP.Checked;

            if (checkBoxERP.Checked)
            {
                if (string.IsNullOrEmpty(inputERPlabel.Text))
                {
                    errorProvider.SetError(inputERPlabel, "Please provide a description that distinguishes markers of ERP periods to classify.");
                    return false;
                }

                p.erp_description = inputERPlabel.Text;

                if (!uint.TryParse(inputERPbefore.Text, out p.erp_before))
                {
                    errorProvider.SetError(inputERPbefore, "Needs to be a positive integer.");
                    return false;
                }
                if (!uint.TryParse(inputERPafter.Text, out p.erp_after))
                {
                    errorProvider.SetError(inputERPafter, "Needs to be a positive integer.");
                    return false;
                }
                if((p.erp_before+p.erp_after)<400)
                {
                    errorProvider.SetError(inputERPbefore, "Minimum duration of measurement to classify: 400ms.");
                    errorProvider.SetError(inputERPafter, "Minimum duration of measurement to classify: 400ms.");
                    return false;
                }
            }

            if (string.IsNullOrEmpty(resultsPathInput.Text))
            {
                errorProvider.SetError(resultsPathInput, "Please provide a results file path.");
                return false;
            }

            if(!Directory.Exists(resultsPathInput.Text))
            {
                errorProvider.SetError(resultsPathInput, "Directory not found.");
                return false;
            }
            p.resultsPath = resultsPathInput.Text;

            if (string.IsNullOrEmpty(resultsFilenameInput.Text))
            {
                errorProvider.SetError(resultsFilenameInput, "Please provide a results file name.");
                return false;
            }

            p.resultsFilenameWC = resultsFilenameInput.Text;
            p.resultsFilename = p.resultsFilenameWC.Replace("$h", AutomationSupport.Application.ActiveNode.HistoryFile.Name).Replace("$n", AutomationSupport.Application.ActiveNode.Name) + ".txt";

            if (File.Exists(p.resultsPath + p.resultsFilename) || (p.csv && File.Exists(p.resultsPath + p.resultsFilename.Replace(".txt", ".csv"))))
            {
                if (MessageDisplay.AskYesNo("NodeIterator::Execute", "Node Iterator", "Results files (.txt and/or .csv) exist, delete them?")
                        == MessageResult.Yes)
                {
                    try
                    {
                        if (File.Exists(p.resultsPath + p.resultsFilename))
                            File.Delete(p.resultsPath + p.resultsFilename);
                        if (p.csv && File.Exists(p.resultsPath + p.resultsFilename.Replace(".txt", ".csv")))
                            File.Delete(p.resultsPath + p.resultsFilename.Replace(".txt", ".csv"));
                        return true;
                    }
                    catch
                    {
                        MessageDisplay.ShowMessage("VIGALL::Execute", "VIGALL", "Could not delete existing results file.");
                        errorProvider.SetError(resultsFilenameInput, "Please provide a new results file name.");
                        return false;
                    }
                }
                else
                {
                    errorProvider.SetError(resultsFilenameInput, "Please provide a new results file name.");
                    return false;
                }
            }
            
            return true;
        }

        private void buttonOK_Click(object sender, EventArgs e)
        {
            // Validate entire dialog.
            if (!ReadParameters(new Parameters())) return;

            DialogResult = DialogResult.OK;
            Close();
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            segmentLength.Enabled = checkBox1.Checked;
            segmentLengthInput.Enabled = checkBox1.Checked;
        }

        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {
            label17.Enabled = checkBox2.Checked;
            label18.Enabled = checkBox2.Checked;
            label19.Enabled = checkBox2.Checked;
            SEMlengthInput.Enabled = checkBox2.Checked;
            SEMthresholdInput.Enabled = checkBox2.Checked;
            SEMbeforeInput.Enabled = checkBox2.Checked;
            label25.Enabled = checkBox2.Checked;
            EOGnameInput.Enabled = checkBox2.Checked;
        }

        private void checkBox3_CheckedChanged(object sender, EventArgs e)
        {
            ECGnameInput.Enabled = checkBox3.Checked;
            label5.Enabled = checkBox3.Checked;
            label11.Enabled = checkBox3.Checked;
            label26.Enabled = checkBox3.Checked;
            minHeartrateInput.Enabled = checkBox3.Checked;
            maxHeartrateInput.Enabled = checkBox3.Checked;
            ECGnameInput.Enabled = checkBox3.Checked;
        }

        private void checkBox5_CheckedChanged(object sender, EventArgs e)
        {
            SCLEDAnameInput.Enabled = checkBox5.Checked;
            label7.Enabled = checkBox5.Checked;
        }

        private void checkBox6_CheckedChanged(object sender, EventArgs e)
        {
            TEMPnameInput.Enabled = checkBox6.Checked;
            label2.Enabled = checkBox6.Checked;
        }

        private void alphapeakInput_TextChanged(object sender, EventArgs e)
        {
            if (!float.TryParse(alphapeakInput.Text, out alphacenterfrequency))
            {
                errorProvider.SetError(alphapeakInput, "Needs to be numeric.");
            }
            else
            {
                if ((alphacenterfrequency < 8) || (alphacenterfrequency > 13))
                    errorProvider.SetError(alphapeakInput, "Needs to be between 8 and 13.");
                else
                {
                    errorProvider.SetError(alphapeakInput, "");
                    updateBaender();
                }
            }
        }

        private void bandBox9_TextChanged(object sender, EventArgs e)
        {
            if (!float.TryParse(bandBox9.Text, out ar))
            {
                errorProvider.SetError(bandBox9, "Needs to be numeric.");
            }
            else
            {
                if ((ar < 0.5) || (ar > 3))
                    errorProvider.SetError(bandBox9, "Needs to be between 0.5 and 3.");
                else
                {
                    errorProvider.SetError(bandBox9, "");
                    updateBaender();
                }
            }
        }

        private void updateBaender()
        {
            if (checkBoxNewBands.Checked)
            {
                bandBox1.Text = "2";
                bandBox2.Text = "4";
                bandBox3.Text = (alphacenterfrequency - ar).ToString();
                bandBox4.Text = "2";
                bandBox5.Text = "4";
                bandBox6.Text = "7";
                bandBox7.Text = (alphacenterfrequency + ar).ToString();
                bandBox8.Text = "7";
            }
            else
            {
                bandBox1.Text = (alphacenterfrequency * 0.2).ToString();
                bandBox2.Text = (alphacenterfrequency * 0.4).ToString();
                bandBox3.Text = (alphacenterfrequency * 0.8).ToString();
                bandBox4.Text = (alphacenterfrequency * 1.2).ToString();
                bandBox5.Text = (alphacenterfrequency * 0.4).ToString();
                bandBox6.Text = (alphacenterfrequency * 0.8).ToString();
                bandBox7.Text = (alphacenterfrequency * 1.2).ToString();
                bandBox8.Text = (alphacenterfrequency * 2.5).ToString();
            }
        }

        private void checkBoxAPF_CheckedChanged(object sender, EventArgs e)
        {
            bandBox9.Enabled = !checkBoxNewBands.Checked;
            if (checkBoxNewBands.Checked)
                label16.Text = "D+T von";
            else
                label16.Text = "Beta von";
            updateBaender();
        }

        private void resetButton_Click(object sender, EventArgs e)
        {
            resultsPathInput.Text = "C:\\Vision\\";
            resultsFilenameInput.Text = "$h $n";
            checkBoxCSV.Checked = true;

            EOGnameInput.Text = "HEOG";
            checkBox2.Checked = false;
            checkBoxAPFauto.Checked = true;
            checkBoxKriterienAnpassen.Checked = true;
            threshold1Input.Text = "100000";
            b23Input.Text = "200000";
            alphapeakInput.Text = "10";
            SEMthresholdInput.Text = "200";
            stageClengthInput.Text = "30";
            checkBox3.Checked = true;
            checkBox4.Checked = false;
            checkBox1.Checked = true;

            checkBoxBatch.Checked = false;
            checkBoxBatchExclude.Checked = false;
            checkBox5.Checked = true;
            checkBox6.Checked = true;
            ECGnameInput.Text = "EKG";
            SCLEDAnameInput.Text = "SCL";
            TEMPnameInput.Text = "TEMP";

            threshold2Input.Text = "2";
            threshold3Input.Text = "1";
            threshold4Input.Text = "4";
            minHeartrateInput.Text = "300";
            maxHeartrateInput.Text = "1500";
            SEMlengthInput.Text = "6000";
            SEMbeforeInput.Text = "6000";

            checkBoxERP.Checked = false;
            inputERPlabel.Text = "ERP";
            inputERPbefore.Text = "100";
            inputERPafter.Text = "1000";

            bandBox1.Text = "2";
            bandBox2.Text = "4";
            bandBox3.Text = "8";
            bandBox4.Text = "12";
            bandBox5.Text = "4";
            bandBox6.Text = "8";
            bandBox7.Text = "12";
            bandBox8.Text = "25";
            bandBox9.Text = "2";
            alphapeakInput.Text = "10";
            segmentLengthInput.Text = "1000";
            kriterienFaktorInput.Text = "2";

            //Privatversion-Optionen
            checkBoxA2PA2T.Checked = false;
            checkBoxNewBands.Checked = false;
            checkBoxROI.Checked = false;
            checkBoxNewTree.Checked = true;

            textBox_UntergrenzeDT.Text = "3";

            alphacenterfrequency = 10;
            ar = 2;

        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            this.linkLabel1.LinkVisited = true;

            System.Diagnostics.Process.Start("https://research.uni-leipzig.de/vigall/");

        }

        //these two can be updated while the window in open
        float alphacenterfrequency = 0;
        float ar = 0;

        private void checkBoxAPF_CheckedChanged_1(object sender, EventArgs e)
        {
            bandBox9.Enabled = !checkBoxNewBands.Checked;
            if (checkBoxNewBands.Checked)
                label16.Text = "TP von";
            else
                label16.Text = "Beta von";
            updateBaender();
        }

        private void checkBoxAPFauto_CheckedChanged(object sender, EventArgs e)
        {
            alphaStartInput.Enabled = checkBoxAPFauto.Checked;
            alphaEndInput.Enabled = checkBoxAPFauto.Checked;
            alphapeakInput.Enabled = !checkBoxAPFauto.Checked;
            checkBoxKriterienAnpassen.Enabled = checkBoxAPFauto.Checked;
            checkBoxKriterienAnpassen.Checked = checkBoxAPFauto.Checked;
            kriterienFaktorInput.Enabled = checkBoxAPFauto.Checked;
        }

        private void checkBoxKriterienAnpassen_CheckedChanged(object sender, EventArgs e)
        {
            threshold1Input.Enabled = !checkBoxKriterienAnpassen.Checked;
            b23Input.Enabled = !checkBoxKriterienAnpassen.Checked;
        }

        private void checkBoxERP_CheckedChanged_1(object sender, EventArgs e)
        {
            inputERPlabel.Enabled = checkBoxERP.Checked;
            inputERPbefore.Enabled = checkBoxERP.Checked;
            inputERPafter.Enabled = checkBoxERP.Checked;
        }

        private void FrmParameterDlg_Load(object sender, EventArgs e)
        {

        }
    }
}