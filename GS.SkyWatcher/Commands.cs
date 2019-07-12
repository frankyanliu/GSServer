﻿/* Copyright(C) 2019  Rob Morgan (robert.morgan.e@gmail.com)

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published
    by the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Reflection;
using System.Text;
using System.Threading;
using GS.Shared;

namespace GS.SkyWatcher
{
    /// <summary>
    /// Skywatcher Commands class
    /// Based on information from Andrew Johansen and the original skywatcher-pacific project 5/31/2011: release C# basic API 1.0
    /// Contructor takes a connected serial object. 
    /// </summary>
    public class Commands
    {
        #region Fields

        private const char _endChar = (char)13;                          // Tailing charactor of command and response.
        private readonly AxisStatus[] _axesStatus = new AxisStatus[2];  // Status and state information for each axis
        private readonly double[] _positions = { 0, 0 };
        private readonly double[] _factorStepToRad = { 0, 0 };          // radians per step based on gear ratio
        private readonly double[] _factorRadToStep = { 0, 0 };          // steps per radian based on gear ratio
        private readonly long[] _breakSteps = new long[2];
        private readonly long[] _stepTimerFreq = new long[2];
        private readonly long[] _peSteps = new long[2];
        private readonly string[] _axisStringVersion = new string[2];   //Readable string version format
        private readonly long[] _axisGearRatios = new long[2];
        private readonly double[] _factorRadRateToInt = { 0, 0 };
        private readonly long[] _lowSpeedGotoMargin = new long[2];
        private readonly long[] _axisVersion = new long[2];             // Axes versions
        private readonly long[] _highSpeedRatio = new long[2];          // HiSpeed multiplier  EQ6Pro, AZEQ5, EQ8 = 16   AZEQ6 = 32
        private const int _threadLockTimeout = 50; // milliseconds

        public DateTime LastI1RunTime { get; private set; }
        public DateTime LastJ2RunTime { get; private set; }
        
        // use for serial event
        //private string IncomingData;
        // number of retries sending the same command
        private int _retryCount;
        // total allowed limit of retries before error
        //private const int _totalRetriesLimit = 20;
        #endregion

        #region Properties

        /// <summary>
        /// Quick check to see if serial is connected and mount is receiving and sending data
        /// </summary> 
        internal bool IsConnected => Serial.IsOpen && MountConnected;

        /// <summary>
        /// Serial object
        /// </summary>
        private SerialPort Serial { get; }

        private bool MountConnected { get; set; }

        #endregion

        #region Methods

        public Commands(SerialPort serial)
        {
            Serial = serial;
            if (serial.IsOpen)
            {
                //Serial.DataReceived += DataReceived;
                //Serial.ErrorReceived += ErrorReceived;
                //Serial.PinChanged += PinReceived;
                MountConnected = true;
            }
        }

        /// <summary>
        /// Load settings from the mount
        /// </summary>
        internal void LoadMountDefaults()
        {
            MountConnected = true;
            InitializeAxes();
            GetAxisVersion(AxisId.Axis1);
            GetAxisVersion(AxisId.Axis2);
            // Inquire Gear Rate
            GetStepsPerRevolution(AxisId.Axis1);
            GetStepsPerRevolution(AxisId.Axis2);
            // Inquire motor high speed ratio
            GetTimerInterruptFreq(AxisId.Axis1);
            GetTimerInterruptFreq(AxisId.Axis2);
            // Inquire motor high speed ratio
            GetHighSpeedRatio(AxisId.Axis1);
            GetHighSpeedRatio(AxisId.Axis2);
            // Inquire Axis Position
            _positions[(int)AxisId.Axis1] = GetAxisPosition(AxisId.Axis1);
            _positions[(int)AxisId.Axis2] = GetAxisPosition(AxisId.Axis2);
            // These two LowSpeedGotoMargin are calculate from slewing for 5 seconds in 128x sidereal rate
            _lowSpeedGotoMargin[(int)AxisId.Axis1] = (long)(640 * Constant.Siderealrate * _factorRadToStep[(int)AxisId.Axis1]);
            _lowSpeedGotoMargin[(int)AxisId.Axis2] = (long)(640 * Constant.Siderealrate * _factorRadToStep[(int)AxisId.Axis2]);
            // Default break steps
            _breakSteps[(int)AxisId.Axis1] = 3500;
            _breakSteps[(int)AxisId.Axis2] = 3500;
        }

        /// <summary>
        /// Common readable format for the axes versions
        /// </summary>
        /// <returns></returns>
        internal string[] GetAxisStringVersions()
        {
            return (string[])_axisStringVersion.Clone();
        }

        /// <summary>
        /// Long format of axes versions
        /// </summary>
        /// <returns></returns>
        internal long[] GetAxisVersions()
        {
            return (long[])_axisVersion.Clone();
        }

        /// <summary>
        /// Gear ratios
        /// </summary>
        /// <returns></returns>
        internal long[] GetStepsPerRevolution()
        {
            return (long[])_axisGearRatios.Clone();
        }

        /// <summary>
        /// Time Fequency per step
        /// </summary>
        /// <returns></returns>
        internal long[] GetStepTimeFreq()
        {
            return (long[])_stepTimerFreq.Clone();
        }

        /// <summary>
        /// High Speed ratio
        /// </summary>
        /// <returns></returns>
        internal long[] GetHighSpeedRatio()
        {
            return (long[])_highSpeedRatio.Clone();
        }

        /// <summary>
        /// margin to move from high speed to low speed
        /// </summary>
        /// <returns></returns>
        internal long[] GetLowSpeedGotoMargin()
        {
            return (long[])_lowSpeedGotoMargin.Clone();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        internal double[] GetFactorRadRateToInt()
        {
            return (double[])_factorRadRateToInt.Clone();
        }

        /// <summary>
        /// Break Steps
        /// </summary>
        /// <returns></returns>
        internal long[] GetBreakSteps()
        {
            return (long[])_breakSteps.Clone();
        }

        /// <summary>
        /// Updates an axis status for slew movements
        /// </summary>
        /// <param name="axis"></param>
        /// <param name="forward"></param>
        /// <param name="highspeed"></param>
        internal void SetSlewing(int axis, bool forward, bool highspeed)
        {
            _axesStatus[axis].SetSlewing(forward, highspeed);
        }

        /// <summary>
        /// Updates an axis status for goto movements
        /// </summary>
        /// <param name="axis"></param>
        /// <param name="forward"></param>
        /// <param name="highspeed"></param>
        internal void SetSlewingTo(int axis, bool forward, bool highspeed)
        {
            _axesStatus[axis].SetSlewingTo(forward, highspeed);
        }

        /// <summary>
        /// return the last known status without polling the mount
        /// </summary>
        /// <param name="axis"></param>
        /// <returns></returns>
        internal AxisStatus GetCacheAxisStatus(AxisId axis)
        {
            return _axesStatus[(int)axis];
        }

        #endregion

        #region Commands

        /// <summary>
        /// a Inquire Grid Per Revolution ":a(*2)", where *2: '1'= CH1, '2' = CH2.
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        private void GetStepsPerRevolution(AxisId axis)
        {
            var response = CmdToAxis(axis, 'a', null);
            var gearRatio = StringToLong(response);
            // There is a issue in the earlier version firmware(Before 2.00) of motor controller MC001.
            // Overwrite the GearRatio reported by the MC for 80GT mount and 114GT mount.
            if (axis == AxisId.Axis1)
            {
                if ((_axisVersion[0] & 0x0000FF) == 0x80)
                {
                    gearRatio = 0x162B97; // for 80GT mount
                }
                if ((_axisGearRatios[0] & 0x0000FF) == 0x82)
                {
                    gearRatio = 0x205318; // for 114GT mount
                }
                _axisGearRatios[0] = gearRatio;
            }
            else
            {
                if ((_axisVersion[1] & 0x0000FF) == 0x80)
                {
                    gearRatio = 0x162B97; // for 80GT mount
                }
                if ((_axisVersion[1] & 0x0000FF) == 0x82)
                {
                    gearRatio = 0x205318; // for 114GT mount
                }
                _axisGearRatios[1] = gearRatio;
            }
            _factorRadToStep[(int)axis] = gearRatio / (2 * Math.PI);
            _factorStepToRad[(int)axis] = 2 * Math.PI / gearRatio;
        }

        /// <summary>
        /// b Inquire Timer Interrupt Freq ":b1".
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        private void GetTimerInterruptFreq(AxisId axis)
        {
            var response = CmdToAxis(axis, 'b', null);

            var timeFreq = StringToLong(response);
            _stepTimerFreq[(int)axis] = timeFreq;

            _factorRadRateToInt[(int)axis] = _stepTimerFreq[(int)axis] / _factorRadToStep[(int)axis];
        }

        /// <summary>
        /// c microsteps from target where the rampdown process begins
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        internal double GetRampDownRange(AxisId axis)
        {
            var response = CmdToAxis(axis, 'c', null);
            var responseString = StringToLong(response);
            return responseString;
        }

        /// <summary>
        /// d Get Current Encoder count
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        internal double GetEncoderCount(AxisId axis)
        {
            var response = CmdToAxis(axis, 'd', null);
            var responseString = StringToLong(response);
            return responseString;
        }

        /// <summary>
        /// e Gets version of the axis
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        private void GetAxisVersion(AxisId axis)
        {
            var response = CmdToAxis(axis, 'e', null);
            var tmpVersion = Convert.ToInt32(StringToLong(response));
            var r = ((tmpVersion & 0xFF) << 16) | ((tmpVersion & 0xFF00)) | ((tmpVersion & 0xFF0000) >> 16);
            if (axis == AxisId.Axis1)
            {
                _axisStringVersion[0] = Convert.ToString(Convert.ToInt32(tmpVersion % 256)) + "." + Convert.ToString(Convert.ToInt32((tmpVersion / 256) % 256)) + "." + Convert.ToString(tmpVersion >> 16);
                _axisVersion[0] = r;
            }
            else
            {
                _axisStringVersion[1] = Convert.ToString(Convert.ToInt32(tmpVersion % 256)) + "." + Convert.ToString(Convert.ToInt32((tmpVersion / 256) % 256)) + "." + Convert.ToString(tmpVersion >> 16);
                _axisVersion[1] = r;
            }
        }

        /// <summary>
        /// e Gets motor card version
        /// </summary>
        internal string GetMotorCardVersion(AxisId axis)
        {
            var response = CmdToAxis(axis, 'e', null);
            return response;
        }

        /// <summary>
        /// f Get the target axis's status as a struct
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <returns></returns>
        internal AxisStatus GetAxisStatus(AxisId axis)
        {
            var response = CmdToAxis(axis, 'f', null);

            //check if at full stop = 1
            if ((response[2] & 0x01) == 0)
            {
                _axesStatus[(int)axis].FullStop = true; 
                _axesStatus[(int) axis].Slewing = false;
                _axesStatus[(int) axis].SlewingTo = false;
                _axesStatus[(int)axis].StepSpeed = "*";
            }
            else
            {
                // Axis is running
                _axesStatus[(int)axis].FullStop = false;
                _axesStatus[(int)axis].Slewing = (response[1] & 0x01) != 0;
                _axesStatus[(int)axis].SlewingTo = (response[1] & 0x01) == 0;
            }
            _axesStatus[(int)axis].SlewingForward = (response[1] & 0x02) == 0;
            _axesStatus[(int)axis].HighSpeed = (response[1] & 0x04) != 0;
            _axesStatus[(int)axis].NotInitialized = (response[3] & 1) == 0;
            _axesStatus[(int)axis].StepSpeed = GetLastSlewSpeed(axis).ToString();
            return _axesStatus[(int)axis];
        }

        /// <summary>
        /// g Inquire High Speed Ratio, EQ6Pro, AZEQ5, EQ8 = 16   AZEQ6 = 32
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        private void GetHighSpeedRatio(AxisId axis)
        {
            var response = CmdToAxis(axis, 'g', null);
            var highSpeedRatio = StringToLong(response);
            _highSpeedRatio[(int)axis] = highSpeedRatio;
        }

        /// <summary>
        /// h Get Current "goto" target
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        internal double GetLastGoToTarget(AxisId axis)
        {
            var response = CmdToAxis(axis, 'h', null);
            var responseString = StringToLong(response);
            return responseString;
        }

        /// <summary>
        /// i Get Current "slew" speed
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        internal long GetLastSlewSpeed(AxisId axis)
        {
            var response = CmdToAxis(axis, 'i', null);
            var responseString = StringToLong(response);
            return responseString;
        }

        /// <summary>
        /// j Gets radians position of an axis
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <returns>Radians of the axis</returns>
        internal double GetAxisPosition(AxisId axis) 
        {
            var response = CmdToAxis(axis, 'j', null);
            var iPosition = StringToLong(response);
            iPosition -= 0x00800000;
            _positions[(int)axis] = StepToAngle(axis, iPosition);
            return _positions[(int)axis];
        }

        /// <summary>
        /// j Gets axis poistion counter
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <returns>Cardinal encoder count</returns>
        internal long GetAxisPositionCounter(AxisId axis)
        {
            var response = CmdToAxis(axis, 'j', null);
            var iPosition = StringToLong(response);
            iPosition -= 0x00800000;
            return iPosition;
        }

        /// <summary>
        /// q Home position 
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>  
        internal long GetHomePosition(AxisId axis)
        {
            var szCmd = LongToHex(0);
            var response = CmdToAxis(axis, 'q', szCmd);
            var position = StringToLong(response);
            return position;
        }

        /// <summary>
        /// q Capabilities
        ///    :qx010000[0D]=ABCDEF[0D]  ie the bitmapped nybbles for current status
        /// A    8  not defined
        ///      4  not defined
        ///      2  PPEC ON
        ///      1  PPEC training in progress,
        /// B    8  supports AZ/EQ
        ///      4  has Home Sensors
        ///      2  supports PPEC
        ///      1  supports dual encoders
        /// C    8  has WIFI
        ///      4  supports half current tracking          // ref :Wx06....
        ///      2  axes slews must start independently     // ie cant use :J3
        ///      1  has polar LED
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>  
        internal string GetCapabilities(AxisId axis)
        {
            var szCmd = LongToHex(1);
            var response = CmdToAxis(axis, 'q', szCmd);
            return response;
        }

        /// <summary>
        /// s Inquire PEC Period ":s(*1)", where *1: '1'= CH1, '2'= CH2, '3'= Both.
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        internal double GetPecPeriod(AxisId axis)
        {
            var response = CmdToAxis(axis, 's', null);

            var pecPeriod = StringToLong(response);
            _peSteps[(int)axis] = pecPeriod;
            return pecPeriod;
        }

        /// <summary>
        /// D Sidereal rate in stepcounts
        /// </summary>
        /// <returns></returns>
        internal long GetSiderealRate(AxisId axis)
        {
            var response = CmdToAxis(axis, 'D', null);
            return StringToLong(response);
        }

        /// <summary>
        /// E Set the target axis position to the specify value
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <param name="radians">raidian value</param>
        internal void SetAxisPosition(AxisId axis, double radians)  
        {
            var newStepIndex = AngleToStep(axis, radians);
            newStepIndex += 0x800000;

            var szCmd = LongToHex(newStepIndex);
            CmdToAxis(axis, 'E', szCmd);

            _positions[(int)axis] = radians;
        }

        /// <summary>
        /// F Initial the target axis
        /// </summary>
        internal void InitializeAxes()  
        {
            CmdToAxis(AxisId.Axis1, 'F', null);
            CmdToAxis(AxisId.Axis2, 'F', null);
        }

        /// <summary>
        /// G Set the different motion mode
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <param name="func">'0' high speed GOTO slewing,'1' low speed slewing mode,'2' low speed GOTO mode,'3' High slewing mode</param>
        /// <param name="direction">0=forward/right, 1=backaward/left</param>
        /// <param name="southernHemisphere">is mount in the south</param>
        internal void SetMotionMode(AxisId axis, int func, int direction, bool southernHemisphere)  
        {
            switch (direction)
            {
                case 0:
                case 2:
                    direction = southernHemisphere ? 2 : 0;
                    break;
                case 1:
                case 3:
                    direction = southernHemisphere ? 3 : 1;
                    break;
                default:
                    return;
            }
            var szCmd = $"{func}{direction}";
            CmdToAxis(axis, 'G', szCmd);
        }

        /// <summary>
        /// H Set the goto target increment
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <param name="stepsCount"></param>
        internal void SetGotoTargetIncrement(AxisId axis, long stepsCount)  
        {
            var cmd = LongToHex(stepsCount);
            CmdToAxis(axis, 'H', cmd);
        }

        /// <summary>
        /// I Set slewing rate, seems to relate to amount of skipped step counts.  
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <param name="stepSpeed">StepSpeed = 1 motor step movement, higher counts means slower movements</param>
        internal void SetStepSpeed(AxisId axis, long stepSpeed)  
        {
            var szCmd = LongToHex(stepSpeed);
            CmdToAxis(axis, 'I', szCmd);
        }

        /// <summary>
        /// J Start motion based on previous settings
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        internal void StartMotion(AxisId axis)  
        {
            CmdToAxis(axis, 'J', null);
        }

        /// <summary>
        /// K Stop the target axis normally
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        internal void AxisStop(AxisId axis)  
        {
            CmdToAxis(axis, 'K', null);
            _axesStatus[(int)axis].SetFullStop();
        }

        /// <summary>
        /// L Stop the target axis instantly
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        internal void AxisStopInstant(AxisId axis)  
        {
            CmdToAxis(axis, 'L', null);
            _axesStatus[(int)axis].SetFullStop();
        }

        /// <summary>
        /// M Set the break point increment
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <param name="stepsCount"></param>
        internal void SetBreakPointIncrement(AxisId axis, long stepsCount)  
        {
            var szCmd = LongToHex(stepsCount);
            CmdToAxis(axis, 'M', szCmd);
        }

        /// <summary>
        /// O on/off trigger
        /// </summary>
        /// <param name="on"></param>
        internal void SetSnapPort(bool on)  
        {
            CmdToAxis(AxisId.Axis1, 'O', on ? "1" : "0");
        }

        /// <summary>
        /// set ST4 guiderate
        /// </summary>
        /// <param name="rate"> 0..4 (1.0, 0.75, 0.50, 0.25, 0.125)</param>
        internal void SetSt4Guiderate(int rate)
        {
            CmdToAxis(AxisId.Axis1, 'P', $"{rate}");
        }

        /// <summary>
        /// S Set absolute goto target 
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <param name="position"></param>
        internal void SetTargetPosition(AxisId axis, double position)  
        {
            var szCmd = LongToHex(Convert.ToInt64(position));
            CmdToAxis(axis, 'S', szCmd);
        }

        /// <summary>
        /// U Set the Break Steps
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <param name="newBrakeSteps"></param>
        internal void SetBreakSteps(AxisId axis, long newBrakeSteps)  
        {
            var szCmd = LongToHex(newBrakeSteps);
            CmdToAxis(axis, 'U', szCmd);
        }

        /// <summary>
        /// W on/off PPEC train
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <param name="on"></param>
        internal void SetPpecTrain(AxisId axis, bool on)  
        {
            var szCmd = LongToHex(1);
            if (on)
            {
                szCmd = LongToHex(0);
            }
            CmdToAxis(axis, 'W', szCmd);
        }

        /// <summary>
        /// W on/off PPEC
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <param name="on"></param>
        internal void SetPpec(AxisId axis, bool on)  
        {
            var szCmd = LongToHex(3);
            if (on)
            {
                szCmd = LongToHex(2);
            }
            CmdToAxis(axis, 'W', szCmd);
        }

        /// <summary>
        /// W on/off encoders
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <param name="on"></param>
        internal void SetEncoders(AxisId axis, bool on)  
        {
            var szCmd = LongToHex(5);
            if (on)
            {
                szCmd = LongToHex(4);
            }
            CmdToAxis(axis, 'W', szCmd);
        }

        /// <summary>
        /// W on/off Full Current Low speed
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <param name="on"></param>
        internal void SetLowSpeedCurrent(AxisId axis, bool on)  
        {
            var szCmd = LongToHex(6);
            if (on)
            {
                szCmd = LongToHex(601);
            }
            CmdToAxis(axis, 'W', szCmd);
        }
        
        /// <summary>
        /// W 7 Set Stride for Slewing
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        internal void SetSlewingStride(AxisId axis)  
        {
            var szCmd = LongToHex(7);
            CmdToAxis(axis, 'W', szCmd);
        }

        /// <summary>
        /// W 8 reset the home position index
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        internal void SetHomePositionIndex(AxisId axis)  
        {
            var szCmd = LongToHex(8);
            CmdToAxis(axis, 'W', szCmd);
        }

        #endregion

        #region SerialIO

        /// <summary>
        /// One communication between mount and client
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <param name="command">The comamnd char set</param>
        /// <param name="cmdDataStr">The data need to send</param>
        /// <returns>The response string from mount</returns>
        private string CmdToAxis(AxisId axis, char command, string cmdDataStr)
        {
            MonitorEntry monitorItem;
            for (var i = 0; i < 5; i++)
            {
                var acquiredLock = false;
                try
                {
                    Monitor.TryEnter(Serial, _threadLockTimeout, ref acquiredLock);
                    if (acquiredLock)
                    {
                        // Code that accesses resources that are protected by the lock.
                        try
                        {
                            string responseString = null;
                            for (var c = 0; c < 3; c++)
                            {
                                Serial.DiscardInBuffer();
                                Serial.DiscardOutBuffer();
                                // send the request
                                var cmdData = SendRequest(axis, command, cmdDataStr);
                                // receive the response
                                responseString = RecieveResponse(axis, command, cmdData);
                                if (!string.IsNullOrEmpty(responseString)) break;
                                _retryCount++;

                                monitorItem = new MonitorEntry
                                {
                                    Datetime = Principles.HiResDateTime.UtcNow,
                                    Device = MonitorDevice.Telescope,
                                    Category = MonitorCategory.Mount,
                                    Type = MonitorType.Warning,
                                    Method = MethodBase.GetCurrentMethod().Name,
                                    Thread = Thread.CurrentThread.ManagedThreadId,
                                    Message =
                                        $"Serial Retry Warning: {_retryCount} {cmdDataStr}"
                                };
                                MonitorLog.LogToMonitor(monitorItem);

                            }

                            // too many retries - serial issue  
                            //if (_retryCount > _totalRetriesLimit)
                            //{
                            //    _retryCount = 0;
                            //throw new MountControlException(ErrorCode.ErrTooManyRetries,
                            //        $"Too Many Serial Retries: {_retryCount}");
                            //}

                            if (string.IsNullOrEmpty(responseString))
                            {
                                SendRequest(AxisId.Axis1, 'K', null);
                                SendRequest(AxisId.Axis2, 'K', null);
                                throw new TimeoutException("Null Response");
                            }
                            MountConnected = true;
                            return responseString;
                        }
                        catch (TimeoutException ex)
                        {
                            MountConnected = false;
                            throw axis == AxisId.Axis1
                                ? new MountControlException(ErrorCode.ErrNoresponseAxis1, "Timeout", ex)
                                : new MountControlException(ErrorCode.ErrNoresponseAxis2, "Timeout", ex);
                        }
                        catch (IOException ex)
                        {
                            MountConnected = false;
                            monitorItem = new MonitorEntry
                                {Datetime = Principles.HiResDateTime.UtcNow, Device=MonitorDevice.Telescope, Category=MonitorCategory.Mount, Type=MonitorType.Error, Method=MethodBase.GetCurrentMethod().Name, Thread=Thread.CurrentThread.ManagedThreadId, Message = $"{ex.Message}, {ex.StackTrace}"};
                            MonitorLog.LogToMonitor(monitorItem);

                            throw new MountControlException(ErrorCode.ErrNotConnected, "IO Error", ex);
                        }
                        catch (Exception ex)
                        {
                            MountConnected = false;
                            monitorItem = new MonitorEntry
                                { Datetime = Principles.HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Mount, Type = MonitorType.Error, Method = MethodBase.GetCurrentMethod().Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{ex.Message}, {ex.StackTrace}" };
                            MonitorLog.LogToMonitor(monitorItem);
                            throw;
                        }
                    }
                    else
                    {
                        // deal with the fact that the lock was not acquired.
                        monitorItem = new MonitorEntry
                            { Datetime = Principles.HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Mount, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod().Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"Lock not acquired #{i} Command:{command} String:{cmdDataStr}" };
                        MonitorLog.LogToMonitor(monitorItem);
                    }
                }
                finally
                {
                    if (acquiredLock)Monitor.Exit(Serial);
                }
                Thread.Sleep(3);
            }
            // deal with the fact that the lock was not acquired.
            monitorItem = new MonitorEntry
                { Datetime = Principles.HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Mount, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod().Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = "Thread Lock Timeout" };
            MonitorLog.LogToMonitor(monitorItem);
            return null;
        }

        /// <summary>
        /// Builds the command string
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <param name="command"></param>
        /// <param name="cmdDataStr"></param>
        private string SendRequest(AxisId axis, char command, string cmdDataStr)
        {
            const char startCharOut = ':';
            if (cmdDataStr == null) cmdDataStr = "";
            const int bufferSize = 20;
            var commandStr = new StringBuilder(bufferSize);
            commandStr.Append(startCharOut);                    // 0: Leading char
            commandStr.Append(command);                         // 1: Length of command( Source, distination, command char, data )
            // Target Device
            commandStr.Append(axis == AxisId.Axis1 ? '1' : '2');// 2: Target Axis
            // Copy command data to buffer
            commandStr.Append(cmdDataStr);
            commandStr.Append(_endChar);                         // CR Character            

            switch (command)
            {
                // store execute start time for any measurements
                case 'J' when axis == AxisId.Axis2:
                    LastJ2RunTime = Principles.HiResDateTime.UtcNow;
                    break;
                case 'I' when axis == AxisId.Axis1:
                    LastI1RunTime = Principles.HiResDateTime.UtcNow;
                    break;
            }

            //Serial.Transmit(commandStr.ToString());
            Serial.Write(commandStr.ToString());

            var monitorItem = new MonitorEntry
                { Datetime = Principles.HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Mount, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod().Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{commandStr}" };
            MonitorLog.LogToMonitor(monitorItem);
            return $"{commandStr}";
        }

        ///// <summary>
        ///// Work for seialport event - meium cpu usage
        ///// </summary>
        ///// <param name="sender"></param>
        ///// <param name="e"></param>
        //private void DataReceived(object sender, SerialDataReceivedEventArgs e)
        //{
        //    IncomingData = RecieveResponse();
        //}

        ///// <summary>
        ///// Errors for seialport event
        ///// </summary>
        ///// <param name="sender"></param>
        ///// <param name="e"></param>
        //private static void ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        //{
        //    var monitorItem = new MonitorEntry
        //        { Datetime = Principles.HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Mount, Type = MonitorType.Error, Method = MethodBase.GetCurrentMethod().Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{e}" };
        //    MonitorLog.LogToMonitor(monitorItem);
        //}

        ///// <summary>
        ///// Reads serial port buffer - medium cpu usage
        ///// </summary>
        //private StringBuilder serialBuffer = new StringBuilder();
        //private const string terminationSequence = "\r";
        //private string DataReceived(string cmdDataStr)
        //{
        //    try
        //    {
        //        var data = Serial.ReadExisting();
        //        string message = null;
        //        serialBuffer.Append(data);
        //        var bufferString = serialBuffer.ToString();
        //        int index;
        //        do
        //        {
        //            index = bufferString.IndexOf(terminationSequence, StringComparison.Ordinal);
        //            if (index <= -1) continue;
        //            message = bufferString.Substring(0, index);
        //            bufferString = bufferString.Remove(0, index + terminationSequence.Length);

        //        } while (index > -1);
        //        serialBuffer = new StringBuilder(bufferString);
        //        return message;
        //    }
        //    catch (Exception ex)
        //    {
        //        Trace.TraceInformation("Retry {0}", ex.Message);
        //        var monitorItem = new MonitorEntry
        //            { Datetime = Principles.HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Mount, Type = MonitorType.Error, Method = MethodBase.GetCurrentMethod().Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{cmdDataStr}, {ex.Message}" };
        //        MonitorLog.LogToMonitor(monitorItem);
        //        throw;
        //    }
        //}

        /// <summary>
        /// Read serial port buffer - skywatcher original source
        /// </summary>
        /// <returns></returns>
        private string RecieveResponse()
        {
            // format "::e1\r=020883\r"
            var mBuffer = new StringBuilder(15);
            var StartReading = false;

            var sw  = Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < Serial.ReadTimeout)
            {
                var data = Serial.ReadExisting();
                foreach (var byt in data)
                {
                    // this code order is important
                    if (byt == '=' || byt == '!' || byt == _endChar) StartReading = true;
                    if (StartReading) mBuffer.Append(byt);
                    if (byt != _endChar) continue;
                    if (!StartReading) continue;
                    return mBuffer.ToString();
                }
                Thread.Sleep(1);
            }
            return null;
        }
        
        /// <summary>
        /// Constructs a string from the responce
        /// </summary>
        /// <returns></returns>
        private string RecieveResponse(AxisId axis, char command, string cmdDataStr)
        {
            //var sw  = Stopwatch.StartNew();
            //while (sw.Elapsed.TotalMilliseconds < 1000)
            //{
            //    // alternative method
            //    //receivedData = DataReceived(cmdDataStr);

            //    receivedData = RecieveResponse();
            //    if (!string.IsNullOrEmpty(receivedData)) break;
                
            //    // alternative using events
            //    //if (!string.IsNullOrEmpty(IncomingData))
            //    //{
            //    //    receivedData = IncomingData;
            //    //    IncomingData = null;
            //    //    break;
            //    //}
            //    //Thread.Sleep(10);
            //}
            //sw.Stop();

            var receivedData = RecieveResponse();

            var monitorItem = new MonitorEntry
                { Datetime = Principles.HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Mount, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod().Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{cmdDataStr},{receivedData}" };
            MonitorLog.LogToMonitor(monitorItem);

            // process incoming data string
            receivedData = receivedData?.Trim();
            receivedData = receivedData?.Replace("\0", string.Empty);
            if (string.IsNullOrEmpty(receivedData)) return null;
            
            switch (receivedData[0].ToString())
            {
                //receive '=DDDDDD [0D]'    or '!D [0D]'
                case "=":  // Normal response
                    break;
                case "!":  // Abnormal response.
                    string errormsg;
                    switch (receivedData)
                    {
                        case "!0":
                            errormsg = "Invalid Command: Command doesnt apply to the model";
                            if (command == 'q') return "=000000";
                            break;
                        case "!1":
                            errormsg = "Invalid Paramcount: Valid command was passed with invalid param count";
                            break;
                        case "!2":
                            errormsg = "Motor not Stopped: Valid command failed to run ( ie sending :G whilst motor is running )";
                            break;
                        case "!3":
                            errormsg = "NonHex Param: Parameter contains a non uppercase Hex Char ";
                            break;
                        case "!4":
                            errormsg = "Not energised: Motor is not energised";
                            break;
                        case "!5":
                            errormsg = "Driver Asleep: card is in sleep mode";
                            break;
                        case "!6":
                            errormsg = "Mount is not tracking";
                            break;
                        case "!7":
                            errormsg = "Unknown";
                            break;
                        case "!8":
                            errormsg = "Invalid PPEC model";
                            break;
                        default:
                            errormsg = "Code Not Found";
                            break;
                    }
                    monitorItem = new MonitorEntry
                        { Datetime = Principles.HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Mount, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod().Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $" Abnormal Response: Axis:{axis}, Command:{command}, CommandStr:{cmdDataStr}, Message: {errormsg}" };
                    MonitorLog.LogToMonitor(monitorItem);
                    receivedData = null;
                    break;
                default:
                    receivedData = null;
                    break;
            }
            return receivedData;
        }

        ///// <summary>
        ///// Sends :e1 to the mounts and evaluates responce to see its an appropriate response.
        ///// </summary>
        //internal void TestSerial()
        //{
        //    var iserror = true;
        //    //Serial.ClearBuffers();
        //    // send the request
        //    SendRequest(AxisId.Axis1, 'e', null);
        //    // receive the response
        //     var responseString = Serial.ReceiveCounted(8);
        //    //var responseString = Serial.ReadLine();

        //    if (responseString.Length > 0)
        //    {
        //        responseString = responseString.Replace("\0", string.Empty).Trim();
        //        // check to see if the response is valid 
        //        switch (responseString[0].ToString())
        //        {
        //            case "=":
        //                iserror = false;
        //                break;
        //            case "!":
        //                iserror = false;
        //                break;
        //        }
        //        // check to see if the number for the mount type is valid
        //        if (!iserror)
        //        {
        //            var parsed = int.TryParse(responseString.Substring(6, 1), out var mountnumber);
        //            if (parsed)
        //            {
        //                if (mountnumber < 0 || mountnumber > 6)iserror = true;
        //            }
        //            else
        //            {
        //                iserror = true;
        //            }
        //        }

        //    }

        //    var monitorItem = new MonitorEntry
        //        { Datetime = Principles.HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Mount, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod().Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $" Response:{responseString}"};
        //    MonitorLog.LogToMonitor(monitorItem);

        //    if (!iserror) return;
        //    throw new MountControlException(ErrorCode.ErrMountNotFound);
        //}

        /// <summary>
        /// Converts the string to a long
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        private long StringToLong(string str)
        {
            try
            {
                long value = 0;
                for (var i = 1; i + 1 < str.Length; i += 2)
                {
                    value += (long)(int.Parse(str.Substring(i, 2), System.Globalization.NumberStyles.AllowHexSpecifier) * Math.Pow(16, i - 1));
                }
                return value;
            }
            catch (FormatException e)
            {
                AxisStop(AxisId.Axis1);
                AxisStop(AxisId.Axis2);
                throw new MountControlException(ErrorCode.ErrInvalidData, "Response Parse Error: " + str, e);
            }
        }

        /// <summary>
        /// Converts a long to Hex command
        /// </summary>
        /// <param name="number"></param>
        /// <returns></returns>
        private static string LongToHex(long number)
        {
            // 31 -> 0F0000
            var a = ((int)number & 0xFF).ToString("X").ToUpper();
            var b = (((int)number & 0xFF00) / 256).ToString("X").ToUpper();
            var c = (((int)number & 0xFF0000) / 256 / 256).ToString("X").ToUpper();

            if (a.Length == 1)
                a = "0" + a;
            if (b.Length == 1)
                b = "0" + b;
            if (c.Length == 1)
                c = "0" + c;
            return a + b + c;
        }

        /// <summary>
        /// Converts steps to angle
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <param name="steps"></param>
        /// <returns></returns>
        internal double StepToAngle(AxisId axis, long steps)
        {
            var a = steps * _factorStepToRad[(int)axis];
            return a;
        }

        /// <summary>
        /// Converts Angle to steps
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <param name="angleInRad"></param>
        /// <returns></returns>
        internal long AngleToStep(AxisId axis, double angleInRad)
        {
            //var a = (long)(angleInRad * _factorRadToStep[(int)axis]);
            var a = (long)Math.Floor(angleInRad * _factorRadToStep[(int)axis]);
            return a;
        }

        #endregion
    }
}
