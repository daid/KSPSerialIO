using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading;
using Microsoft.Win32;
using System.Runtime.InteropServices;

using OpenNETCF.IO.Ports;
using UnityEngine;
using KSP.IO;

namespace KSPSerialIO
{
    public delegate void SerialLineReceivedEvent(string line);

    public class SerialPortLineReader
    {
        private SerialPort port;
        private string received_line;
        public event SerialLineReceivedEvent line_received;

        public SerialPortLineReader(string portName, int baudrate)
        {
            port = new SerialPort(portName, baudrate, Parity.None, 8, StopBits.One);
            port.ReceivedBytesThreshold = 1;
            port.ReceivedEvent += Port_ReceivedEvent;
        }

        public void open()
        {
            port.Open();
        }

        public void close()
        {
            port.Close();
        }

        private void Port_ReceivedEvent(object sender, SerialReceivedEventArgs e)
        {
            while (port.BytesToRead > 0)
            {
                char c = (char)port.ReadByte();
                if (c == '\n')
                {
                    line_received.Invoke(received_line);
                    received_line = "";
                }
                else
                    received_line += c;
            }
        }
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class KSPSerialIO : MonoBehaviour
    {
        private Vessel activeVessel;
        private static SerialPortLineReader serialPort;

        private ScreenMessageStyle KSPIOScreenStyle = ScreenMessageStyle.UPPER_RIGHT;

        private bool[] last_button_down = new bool[128];

        private bool[] button_down_raw = new bool[128];
        private int joystick_x_raw, joystick_y_raw;
        private int joystick_x_dead_min = 370;
        private int joystick_x_dead_max = 405;
        private int joystick_y_dead_min = 610;
        private int joystick_y_dead_max = 635;

        void Awake()
        {
            ScreenMessages.PostScreenMessage("IO awake", 10f, KSPIOScreenStyle);
        }

        void Start()
        {
            Debug.Log("KSPSerialIO: Start");
            joystick_x_raw = joystick_x_dead_min;
            joystick_y_raw = joystick_y_dead_min;
            RenderingManager.AddToPostDrawQueue(0, onDraw);

            if (serialPort == null)
                serialPort = new SerialPortLineReader("\\\\.\\COM160", 115200);
            try
            {
                serialPort.open();
            }
            catch (Exception)
            {
                Debug.Log("KSPSerialIO: Failed to open serial port...");
            }
            serialPort.line_received += gotLine;
        }

        void onDraw()
        {
            string status = "";
            if (serialPort == null)
                status = "No serial port open";
            else
            {
                status = joystick_x_raw + "," + joystick_y_raw;
                for (int n = 0; n < button_down_raw.Length; n++)
                    if (button_down_raw[n])
                        status += " " + n;
            }
            GUI.Label(new Rect(50, 50, 500, 500), status);
        }

        private void gotLine(string line)
        {
            string[] parts = line.Split(' ');
            joystick_x_raw = int.Parse(parts[parts.Length - 2]);
            joystick_y_raw = int.Parse(parts[parts.Length - 1]);

            for (int n = 0; n < button_down_raw.Length; n++)
                button_down_raw[n] = false;
            for(int n = 0; n < parts.Length - 2; n++)
            {
                int index = int.Parse(parts[n]);
                button_down_raw[index] = true;
            }
            //Inverted buttons.
            button_down_raw[35] = !button_down_raw[35];
            button_down_raw[37] = !button_down_raw[37];
            button_down_raw[39] = !button_down_raw[39];
            button_down_raw[41] = !button_down_raw[41];
            button_down_raw[43] = !button_down_raw[43];
        }

        void Update()
        {
            if (FlightGlobals.ActiveVessel != null)
            {
                //If the current active vessel is not what we were using, we need to remove controls from the old 
                //vessel and attache it to the current one
                if (activeVessel != FlightGlobals.ActiveVessel)
                {
                    if (activeVessel != null)
                        activeVessel.OnFlyByWire -= AxisInput;
                    activeVessel = FlightGlobals.ActiveVessel;
                    activeVessel.OnFlyByWire += AxisInput;
                    Debug.Log("KSPSerialIO: ActiveVessel changed");
                }

                for (int n = 0; n < last_button_down.Length; n++)
                {
                    if (last_button_down[n] != button_down_raw[n])
                    {
                        last_button_down[n] = button_down_raw[n];
                        buttonChanged(n, button_down_raw[n]);
                    }
                }
            }
            else
            {
                Debug.Log("KSPSerialIO: ActiveVessel not found");
                //ActiveVessel.OnFlyByWire -= new FlightInputCallback(AxisInput);
            }
        }

        private void buttonChanged(int index, bool down)
        {
            switch (index)
            {
                case 15://Joystick fire button
                    break;
                case 16://Joystick top button
                    break;
                case 17://Decrease trottle
                    break;
                case 18://Increase trottle
                    break;
                case 25://SAS
                    FlightGlobals.ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.SAS, down);
                    updateAutoPilotMode();
                    break;
                case 27://Gear
                    FlightGlobals.ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Gear, down);
                    break;
                case 33://Lights
                    FlightGlobals.ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Light, down);
                    break;
                
                case 24://Docking or staging mode
                    if (down)
                        FlightUIModeController.Instance.SetMode(FlightUIMode.DOCKING);
                    else
                        FlightUIModeController.Instance.SetMode(FlightUIMode.STAGING);
                    break;
                case 29://Breaks
                    FlightGlobals.ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, down);
                    break;
                case 19://???
                    break;

                case 31://Launch button
                    if (down)
                        Staging.ActivateNextStage();
                    FlightGlobals.ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Stage, down);
                    break;

                case 30:
                case 32:
                case 26:
                case 20:
                case 28:
                    updateAutoPilotMode();
                    break;

                case 35:
                    if (down)
                        TimeWarp.SetRate(TimeWarp.CurrentRateIndex - 1, false);
                    break;
                case 53:
                    if (down)
                        TimeWarp.SetRate(TimeWarp.CurrentRateIndex + 1, false);
                    break;

                case 34:
                    FlightGlobals.ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom01, down);
                    break;
                case 36:
                    FlightGlobals.ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom02, down);
                    break;
                case 38:
                    FlightGlobals.ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom03, down);
                    break;
                case 40:
                    FlightGlobals.ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom04, down);
                    break;
                case 42:
                    FlightGlobals.ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom05, down);
                    break;
                case 44:
                    FlightGlobals.ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom06, down);
                    break;
                case 46:
                    FlightGlobals.ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom07, down);
                    break;
                case 48:
                    FlightGlobals.ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom08, down);
                    break;
                case 50:
                    FlightGlobals.ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom09, down);
                    break;
                case 52:
                    FlightGlobals.ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom10, down);
                    break;

                case 43:
                    if (down && MuMechModuleHullCamera.sCurrentHandler != null)
                        MuMechModuleHullCamera.sCurrentHandler.NextCameraAction(null);
                    break;
            }
        }

        private void updateAutoPilotMode()
        {
            if (activeVessel == null || activeVessel.Autopilot == null)
                return;
            if (!button_down_raw[25])
                return;
            if (button_down_raw[30])
            {
                if (button_down_raw[32])
                    activeVessel.Autopilot.SetMode(VesselAutopilot.AutopilotMode.Retrograde);
                else
                    activeVessel.Autopilot.SetMode(VesselAutopilot.AutopilotMode.Prograde);
            }
            else if (button_down_raw[26])
            {
                if (button_down_raw[32])
                    activeVessel.Autopilot.SetMode(VesselAutopilot.AutopilotMode.RadialOut);
                else
                    activeVessel.Autopilot.SetMode(VesselAutopilot.AutopilotMode.RadialIn);
            }
            else if (button_down_raw[20])
            {
                if (button_down_raw[32])
                    activeVessel.Autopilot.SetMode(VesselAutopilot.AutopilotMode.Antinormal);
                else
                    activeVessel.Autopilot.SetMode(VesselAutopilot.AutopilotMode.Normal);
            }
            else if (button_down_raw[28])
            {
                if (button_down_raw[32])
                    activeVessel.Autopilot.SetMode(VesselAutopilot.AutopilotMode.Target);
                else
                    activeVessel.Autopilot.SetMode(VesselAutopilot.AutopilotMode.AntiTarget);
            }
            else
            {
                if (button_down_raw[32])
                    activeVessel.Autopilot.SetMode(VesselAutopilot.AutopilotMode.Maneuver);
                else
                    activeVessel.Autopilot.SetMode(VesselAutopilot.AutopilotMode.StabilityAssist);
            }
            
            VesselAutopilotUI ui = FindObjectOfType<VesselAutopilotUI>();
            if (ui != null)
            {
                for(int n=0; n<ui.modeButtons.Length; n++)
                {
                    VesselAutopilot.AutopilotMode mode = (VesselAutopilot.AutopilotMode)(n);
                    RUIToggleButton.ButtonState state = RUIToggleButton.ButtonState.FALSE;
                    if (!FlightGlobals.ActiveVessel.Autopilot.CanSetMode(mode))
                        state = RUIToggleButton.ButtonState.DISABLED;
                    if (mode == activeVessel.Autopilot.Mode)
                        state = RUIToggleButton.ButtonState.TRUE;
                    if (state != ui.modeButtons[n].State)
                    {
                        switch (state)
                        {
                            case RUIToggleButton.ButtonState.TRUE:
                                ui.modeButtons[n].SetTrue(false, false);
                                ui.modeButtons[n].Enable(false);
                                break;
                            case RUIToggleButton.ButtonState.FALSE:
                                ui.modeButtons[n].SetFalse(false);
                                ui.modeButtons[n].Enable(false);
                                break;
                            case RUIToggleButton.ButtonState.DISABLED:
                                ui.modeButtons[n].Disable(false);
                                break;
                        }
                    }
                }
            }
        }

        private void AxisInput(FlightCtrlState s)
        {
            float x = 0.0f;
            float y = 0.0f;
            if (joystick_x_raw < joystick_x_dead_min)
                x = (float)(joystick_x_raw - joystick_x_dead_min) / 400.0f;
            if (joystick_x_raw > joystick_x_dead_max)
                x = (float)(joystick_x_raw - joystick_x_dead_max) / 400.0f;
            if (joystick_y_raw < joystick_y_dead_min)
                y = (float)(joystick_y_raw - joystick_y_dead_min) / 400.0f;
            if (joystick_y_raw > joystick_y_dead_max)
                y = (float)(joystick_y_raw - joystick_y_dead_max) / 400.0f;
            x = Math.Max(-1.0f, Math.Min(1.0f, x));
            y = Math.Max(-1.0f, Math.Min(1.0f, y));

            if (button_down_raw[24])
            {
                //Docking mode
                if (button_down_raw[16])
                    s.X = -x;
                else
                    s.Z = -x;
                s.Y = y;
            }
            else
            {
                //staging mode
                if (button_down_raw[16])
                {
                    if (x != 0.0f)
                        s.roll = -x;
                }
                else
                {
                    if (x != 0.0f)
                        s.yaw = -x;
                }
                if (y != 0.0f)
                    s.pitch = y;
            }
        }

        void FixedUpdate()
        {
            if (button_down_raw[24])
            {
                //Docking mode
                FlightInputHandler.state.mainThrottle = 0.0f;
            }
            else
            {
                //Staging mode
                if (button_down_raw[17])
                    FlightInputHandler.state.mainThrottle -= 0.2f * Time.fixedDeltaTime;
                if (button_down_raw[18])
                    FlightInputHandler.state.mainThrottle += 0.2f * Time.fixedDeltaTime;
            }
            FlightInputHandler.state.mainThrottle = Math.Max(-1.0f, Math.Min(1.0f, FlightInputHandler.state.mainThrottle));
        }

        void OnDestroy()
        {
            Debug.Log("KSPSerialIO: OnDestroy");
            RenderingManager.RemoveFromPostDrawQueue(0, onDraw);
           
            if (serialPort != null)
                serialPort.line_received -= gotLine;

            if (activeVessel)
                activeVessel.OnFlyByWire -= AxisInput;
        }
    }
}
