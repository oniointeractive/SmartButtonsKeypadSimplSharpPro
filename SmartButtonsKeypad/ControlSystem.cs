using System;
using Crestron.SimplSharp;                          	// For Basic SIMPL# Classes
using Crestron.SimplSharpPro;                       	// For Basic SIMPL#Pro classes
using Crestron.SimplSharpPro.CrestronThread;        	// For Threading
using Crestron.SimplSharpPro.Diagnostics;		    	// For System Monitor Access
using Crestron.SimplSharpPro.DeviceSupport;         	// For Generic Device Support
using Crestron.SimplSharpPro.UI;                        // For Touch panel
using System.Collections.Generic;
using System.IO;

namespace SmartButtonsKeypad
{
    public class ControlSystem : CrestronControlSystem
    {
        
        Tsw1060 myTouchScreen;
        private string keypadText = "";
        private const int password = 123456;

        // SmartObject objects
        private const ushort SgKeypad = 1;
        
        /// <summary>
        /// ControlSystem Constructor. Starting point for the SIMPL#Pro program.
        /// Use the constructor to:
        /// * Initialize the maximum number of threads (max = 400)
        /// * Register devices
        /// * Register event handlers
        /// * Add Console Commands
        /// 
        /// Please be aware that the constructor needs to exit quickly; if it doesn't
        /// exit in time, the SIMPL#Pro program will exit.
        /// 
        /// You cannot send / receive data in the constructor
        /// </summary>
        public ControlSystem()
            : base()
        {
            try
            {
                Thread.MaxNumberOfUserThreads = 20;

                //Subscribe to the controller events (System, Program, and Ethernet)
                CrestronEnvironment.SystemEventHandler += new SystemEventHandler(_ControllerSystemEventHandler);
                CrestronEnvironment.ProgramStatusEventHandler += new ProgramStatusEventHandler(_ControllerProgramEventHandler);
                CrestronEnvironment.EthernetEventHandler += new EthernetEventHandler(_ControllerEthernetEventHandler);
            }
            catch (Exception e)
            {
                ErrorLog.Error("Error in the constructor: {0}", e.Message);
            }
        }

        /// <summary>
        /// InitializeSystem - this method gets called after the constructor 
        /// has finished. 
        /// 
        /// Use InitializeSystem to:
        /// * Start threads
        /// * Configure ports, such as serial and verisports
        /// * Start and initialize socket connections
        /// Send initial device configurations
        /// 
        /// Please be aware that InitializeSystem needs to exit quickly also; 
        /// if it doesn't exit in time, the SIMPL#Pro program will exit.
        /// </summary>
        public override void InitializeSystem()
        {
            try
            {
                myTouchScreen = new Tsw1060(0x03, this);
                if (myTouchScreen.Register() == eDeviceRegistrationUnRegistrationResponse.Success)

                {
                    // Automatically adds method if double tap TAB after += sign
                    myTouchScreen.SigChange += MyTouchScreen_SigChange;
                    LoadUserInterfaceSmartObjectGraphics(myTouchScreen);
                }
                else
                {
                    ErrorLog.Error("Touch panel at ID {0} unable to register.", myTouchScreen.ID);
                }
            }
            catch (Exception e)
            {
                ErrorLog.Error("Error in InitializeSystem: {0}", e.Message);
            }
        }
        
        private void MyTouchScreen_SigChange(BasicTriList currentDevice, SigEventArgs args) // If any button is pressed
        {
            myTouchScreen.BooleanInput[6].BoolValue = myTouchScreen.Home.State == eButtonState.Pressed; // Home Button
            if (args.Sig.BoolValue && args.Sig.Number == 8) // Return Button is Pressed
            {
                myTouchScreen.BooleanInput[6].Pulse();
            }
        }

        private void LoadUserInterfaceSmartObjectGraphics(BasicTriListWithSmartObject currentDevice)
        {
            try
            {
                const string location = "/user/SmartButton.sgd";
                if (!File.Exists(location)) return;
                currentDevice.LoadSmartObjects(location);
                foreach (KeyValuePair<uint, SmartObject> kvp in currentDevice.SmartObjects)
                {
                    kvp.Value.SigChange += new SmartObjectSigChangeEventHandler(SmartObject_SigChange);
                    myTouchScreen.StringInput[1].StringValue = "Enter the password";
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        private void SmartObject_SigChange(GenericBase currentDevice, SmartObjectEventArgs args)
        {
            var item = (BasicTriListWithSmartObject)currentDevice;
            if(args.SmartObjectArgs.ID == SgKeypad)
            {
                SmartObject_KeyPad_SigChange(item, args);
            }
        }

        private void SmartObject_KeyPad_SigChange(BasicTriListWithSmartObject currentDevice, SmartObjectEventArgs args)
        {
            if (!args.Sig.BoolValue) return;
            if (myTouchScreen.StringInput[1].StringValue == "Wrong password!" 
                || myTouchScreen.StringInput[1].StringValue == "Enter the password") 
                myTouchScreen.StringInput[1].StringValue = "";
            switch (args.Sig.Number)
            {
                case 10:        // 0 is Pressed
                    keypadText += "0";
                    myTouchScreen.StringInput[1].StringValue += "* ";
                    break;
                case 11:        // Delete is Pressed
                    if (keypadText.Length > 0
                        && myTouchScreen.StringInput[1].StringValue != "Wrong password!"
                        && myTouchScreen.StringInput[1].StringValue != "Enter the password")
                    {
                        keypadText = keypadText.Substring(0, keypadText.Length - 1);
                        myTouchScreen.StringInput[1].StringValue = 
                            myTouchScreen.StringInput[1].StringValue
                                .Substring(0, myTouchScreen.StringInput[1].StringValue.Length - 2);
                    }
                    break;
                case 12:        // Enter is Pressed
                    if(keypadText == password.ToString())
                    {
                        keypadText = "";
                        myTouchScreen.StringInput[1].StringValue = "Enter the password";
                        myTouchScreen.BooleanInput[7].Pulse();
                    }
                    else
                    {
                        myTouchScreen.StringInput[1].StringValue = "Wrong password!";
                        keypadText = "";
                    }
                    break;
                default:        // 1 to 9
                    keypadText += (args.Sig.Number).ToString()[0];
                    myTouchScreen.StringInput[1].StringValue += "* ";
                    break;
            }
        }

        /// <summary>
        /// Event Handler for Ethernet events: Link Up and Link Down. 
        /// Use these events to close / re-open sockets, etc. 
        /// </summary>
        /// <param name="ethernetEventArgs">This parameter holds the values 
        /// such as whether it's a Link Up or Link Down event. It will also indicate 
        /// wich Ethernet adapter this event belongs to.
        /// </param>
        void _ControllerEthernetEventHandler(EthernetEventArgs ethernetEventArgs)
        {
            switch (ethernetEventArgs.EthernetEventType)
            {//Determine the event type Link Up or Link Down
                case (eEthernetEventType.LinkDown):
                    //Next need to determine which adapter the event is for. 
                    //LAN is the adapter is the port connected to external networks.
                    if (ethernetEventArgs.EthernetAdapter == EthernetAdapterType.EthernetLANAdapter)
                    {
                        //
                    }
                    break;
                case (eEthernetEventType.LinkUp):
                    if (ethernetEventArgs.EthernetAdapter == EthernetAdapterType.EthernetLANAdapter)
                    {

                    }
                    break;
            }
        }

        /// <summary>
        /// Event Handler for Programmatic events: Stop, Pause, Resume.
        /// Use this event to clean up when a program is stopping, pausing, and resuming.
        /// This event only applies to this SIMPL#Pro program, it doesn't receive events
        /// for other programs stopping
        /// </summary>
        /// <param name="programStatusEventType"></param>
        void _ControllerProgramEventHandler(eProgramStatusEventType programStatusEventType)
        {
            switch (programStatusEventType)
            {
                case (eProgramStatusEventType.Paused):
                    //The program has been paused.  Pause all user threads/timers as needed.
                    break;
                case (eProgramStatusEventType.Resumed):
                    //The program has been resumed. Resume all the user threads/timers as needed.
                    break;
                case (eProgramStatusEventType.Stopping):
                    //The program has been stopped.
                    //Close all threads. 
                    //Shutdown all Client/Servers in the system.
                    //General cleanup.
                    //Unsubscribe to all System Monitor events
                    break;
            }

        }

        /// <summary>
        /// Event Handler for system events, Disk Inserted/Ejected, and Reboot
        /// Use this event to clean up when someone types in reboot, or when your SD /USB
        /// removable media is ejected / re-inserted.
        /// </summary>
        /// <param name="systemEventType"></param>
        void _ControllerSystemEventHandler(eSystemEventType systemEventType)
        {
            switch (systemEventType)
            {
                case (eSystemEventType.DiskInserted):
                    //Removable media was detected on the system
                    break;
                case (eSystemEventType.DiskRemoved):
                    //Removable media was detached from the system
                    break;
                case (eSystemEventType.Rebooting):
                    //The system is rebooting. 
                    //Very limited time to preform clean up and save any settings to disk.
                    break;
            }

        }
    }
}
