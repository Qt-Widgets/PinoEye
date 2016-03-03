//*******************************************************************************
//******                 www.trossenrobotics.com                           ******
//******          Email: alexw@trossenrobotics.com                         ******
//******        Program: USB Joystick Controller using DirectX DirectInput ******
//******        Version: 1.1                                               ******
//******        Created: 3/20/05 ported from 7/07/05 vb Express version    ******
//******         Author: Alex Ward                                         ******                 
//******          Usage: Free to use whenever, wherever, however           ******
//******                 you want, just give credit where                  ******
//******                 credit is due.                                    ******
//******************************************************************************* 

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.DirectX;
using Microsoft.DirectX.DirectInput;


namespace CsharpUSBJoystick
{
    public class USB_Joystick 
    {

#region Member Variables

        private JoystickState _state = new JoystickState();
        private Device _applicationDevice = null;
        private int _intThresholdMin;
        private int _intThresholdMax;
        private double _dblThresholdPerc;
        private int _intRangeMin;
        private int _intRangeMax;        

#endregion


#region Constructor

        /// <summary>
        /// Creates a new USB_Joystick object and initializes the minimum and maximum range values of the joystick
        /// along with the threshold percentage.
        /// </summary>   
        /// <param name="rangeMin">The minimum value for the range of the joystick.</param>
        /// <param name="rangeMax">The maximum value for the range of the joystick.</param> 
        /// <param name="thresholdPerc">The threshold percentage. Value must be between 0.0 and .99.</param>
        /// <param name="form">Form to set the cooperative level for the joystick.</param>
        public USB_Joystick(int rangeMin, int rangeMax, double thresholdPerc, System.Windows.Forms.Form form)
        {
            if (thresholdPerc < 0 | thresholdPerc > 0.99) 
            {
                throw new ArgumentOutOfRangeException("Value must be between 0.0 and .99!");
            }
            
            _dblThresholdPerc = thresholdPerc;
            _intRangeMin = rangeMin;
            _intRangeMax = rangeMax;

            if (!(InitDirectInput(form)))
            {
                throw new DirectXException("Couldn't intilize Joystick");                
            }

            PresetValues();
        }


        /// <summary>
        /// Creates a new USB_Joystick object and initializes the minimum and maximum range values of the joystick, 
        /// along with setting the threshold percentage to a default value.       
        /// </summary>
        /// <param name="rangeMin">The minimum value for the range of the joystick.</param>
        /// <param name="rangeMax">The maximum value for the range of the joystick.</param>
        /// <param name="form">Form to set the cooperative level for the joystick.</param>
        public USB_Joystick(int rangeMin, int rangeMax, System.Windows.Forms.Form form) : 
            this(rangeMin, rangeMax, .10, form) {}


        /// <summary>
        /// Creates a new USB_Joystick object and sets the minimum/maximum range values along the threshold
        /// percentage of the joystick to default values.       
        /// <param name="form">Form to set the cooperative level for the joystick.</param>
        /// </summary>        
        public USB_Joystick(System.Windows.Forms.Form form) : this(0, 1000, .10, form) {}


#endregion

        
#region Properties

        /// <summary>
        /// Gets or sets the threshold based on a percentage scale.
        /// </summary>
        public double ThresholdPerc 
        {
            get
            {
                return _dblThresholdPerc;
            }
            set 
            {
                _dblThresholdPerc = value;
                SetThreshold();
            }
        }
        

        /// <summary>
        /// Returns the minimum value of the threshold.
        /// </summary>
        public int ThresholdMin 
        {
            get 
            {
                return _intThresholdMin;
            }
        }


        /// <summary>
        /// Returns the current Joystick state.
        /// </summary>
        public JoystickState State
        {
            get
            {
                return _state;
            }
        }        


        /// <summary>
        /// Returns the maximum value of the threshold.
        /// </summary>
        public int ThresholdMax 
        {
            get 
            {
                return _intThresholdMax;
            }
        }
        
        
        /// <summary>
        /// Returns the raw value of the joystick's X and Y coordinates converted into a percent.
        /// </summary>
        public System.Drawing.PointF GetRawXYPerc 
        {
            get 
            {                
                return new System.Drawing.PointF(((float)(_state.Y - _intRangeMin)/ (_intRangeMax - _intRangeMin)), 
                    ((float)(_state.Z - _intRangeMin) / (_intRangeMax - _intRangeMin)));
            }
        }


        /// <summary>
        /// Returns the number of buttons on the joystick.
        /// </summary>
        public int NumberOfButtons
        {
            get
            {
                return _applicationDevice.Caps.NumberButtons;
            }
        }

#endregion
      
     
        /// <summary>
        /// Returns the value of the specified button.
        /// </summary>
        /// <param name="index">Index of the button being pressed.</param>        
        public string ButtonValue(int index)
        {   
            return _state.GetButtons().GetValue(index).ToString();            
        }


        /// <summary>
        /// Returns the state of a button.
        /// </summary>
        /// <param name="index">The index of the button to be used to test whether it has been pressed.</param>        
        public bool ButtonPressed(int index)
        {
            byte[] buttons = _state.GetButtons();
            int count = 1;
            foreach (byte b in buttons)
            {
                if (count == index)
                {
                    if (b != 0)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                count += 1;
            }
            return false;
        }

   
        /// <summary>
        /// Returns true if the joystick's current X position is outside of the threshold.
        /// </summary>        
        public bool IsXOutsideThreshold()
        {   
            if (((_state.Y > _intThresholdMax) | (_state.Y < _intThresholdMin))) 
            {
                return true;
            }                 
            else 
            {
                return false;
            }            
        }
             

        /// <summary>
        /// Returns true if the joystick's current Y position is outside of the threshold.
        /// </summary>        
        public bool IsYOutsideThreshold() 
        {
            if (((_state.Z > _intThresholdMax) | (_state.Z < _intThresholdMin))) 
            {
                return true;
            } 
            else 
            {
                return false;
            }            
        }
             

        /// <summary>
        /// Returns true if the joystick's current position is inside the threshold    
        /// </summary> 
        public bool IsXYInsideThreshold 
        {
            get 
            {
                if ((!(IsXOutsideThreshold())) & (!(IsYOutsideThreshold())))
                {
                    return true;
                } 
                else 
                {
                    return false;
                }
            }
        }
             

        /// <summary>
        /// Sets the threshold of the X and Y joystick values.
        /// </summary>
        private void SetThreshold()
        {
            _intThresholdMax = Convert.ToInt32((((_intRangeMax - _intRangeMin) * 
                (_dblThresholdPerc / 2)) + (((_intRangeMax - _intRangeMin) / 2) + _intRangeMin)));
            
            _intThresholdMin = Convert.ToInt32((((_intRangeMax - _intRangeMin) / 2) + _intRangeMin) - 
                ((_intRangeMax - _intRangeMin) * (_dblThresholdPerc / 2)));
        }

              
        /// <summary>
        /// Acquires the joystick if necessary, and starts polling the joystick for information.
        /// Uses the same concepts behind the DirectX 9 SDK Joystick sample download.
        /// </summary>
        public void GetData()
        {
            if (null == _applicationDevice) 
            {         
                return;
            }
            try 
            {
                _applicationDevice.Poll();
            } 
            catch (InputException inputex) 
            {
                if (inputex is NotAcquiredException | inputex is InputLostException) 
                {
                    try 
                    {
                        _applicationDevice.Acquire();
                    } 
                    catch 
                    {
                        return;
                    }
                }
            }
            try 
            {
                _state = _applicationDevice.CurrentJoystickState;               
            } 
            catch 
            {
                return;
            }
        }
             

        /// <summary>
        /// Finds the first joystick attached, and initializes it.
        /// Uses the same concepts behind the DirectX 9 Joystick sample download.
        /// </summary>
        /// <param name="Form1">Form to set the cooperative level for the joystick.</param>
        /// <returns>Returns true if the joystick was successfully created.</returns>
        private bool InitDirectInput(System.Windows.Forms.Form Form1)
        {
            foreach (DeviceInstance instance in 
                Manager.GetDevices(DeviceClass.GameControl, EnumDevicesFlags.AttachedOnly)) 
            {
                _applicationDevice = new Device(instance.InstanceGuid);
                break;
            }
                        
            if ((_applicationDevice == null)) 
            {
                throw new DirectXException("Unable to initialize the joystick device.", new Exception(
                    "No joystick or compatible joystick was was found attached to the computer."));                
            }
            
            _applicationDevice.SetDataFormat(DeviceDataFormat.Joystick);
            _applicationDevice.SetCooperativeLevel(Form1, 
                CooperativeLevelFlags.NonExclusive | CooperativeLevelFlags.Foreground);

            foreach (DeviceObjectInstance d in _applicationDevice.Objects) 
            {
                if (0 != (d.ObjectId & System.Convert.ToInt32(DeviceObjectTypeFlags.Axis))) 
                {
                    _applicationDevice.Properties.SetRange(ParameterHow.ById, d.ObjectId, 
                        new InputRange(_intRangeMin, _intRangeMax));
                }
            }
            return true;
        }
             

        /// <summary>
        /// Presets all of the values that are required to use an object of the USB_Joystick class.
        /// </summary>
        public void PresetValues()
        {         
            SetThreshold();
        }
          
   
        /// <summary>
        /// Unacquires the device
        /// </summary>
        public void UnacquireAppDevice()
        {
            if (!(null == _applicationDevice)) 
            {
                _applicationDevice.Unacquire();
            }
        }
             

        /// <summary>
        /// Calls the methods that are used to dispose of this object, and all objects contained within.
        /// </summary>
        public void Dispose()
        {
            UnacquireAppDevice();            
            _applicationDevice.Dispose();                
        }                  
    }
}