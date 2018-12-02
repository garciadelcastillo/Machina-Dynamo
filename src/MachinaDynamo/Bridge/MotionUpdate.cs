﻿using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Web.Script.Serialization;

using Autodesk.DesignScript.Runtime;
using Autodesk.DesignScript.Interfaces;
using Autodesk.DesignScript.Geometry;

using Machina;
using MAction = Machina.Action;
using MPoint = Machina.Point;
using MVector = Machina.Vector;
using MOrientation = Machina.Orientation;
using MTool = Machina.Tool;
using System.IO;
using System.Collections.Generic;
using System;
using System.Windows.Interop;
using WebSocketSharp;
using WebSocketSharp.Net;
using WebSocketSharp.Net.WebSockets;
using WebSocketSharp.Server;

namespace MachinaDynamo
{
    //  ███╗   ███╗ ██████╗ ████████╗██╗ ██████╗ ███╗   ██╗
    //  ████╗ ████║██╔═══██╗╚══██╔══╝██║██╔═══██╗████╗  ██║
    //  ██╔████╔██║██║   ██║   ██║   ██║██║   ██║██╔██╗ ██║
    //  ██║╚██╔╝██║██║   ██║   ██║   ██║██║   ██║██║╚██╗██║
    //  ██║ ╚═╝ ██║╚██████╔╝   ██║   ██║╚██████╔╝██║ ╚████║
    //  ╚═╝     ╚═╝ ╚═════╝    ╚═╝   ╚═╝ ╚═════╝ ╚═╝  ╚═══╝
    //                                                     
    //  ██╗   ██╗██████╗ ██████╗  █████╗ ████████╗███████╗ 
    //  ██║   ██║██╔══██╗██╔══██╗██╔══██╗╚══██╔══╝██╔════╝ 
    //  ██║   ██║██████╔╝██║  ██║███████║   ██║   █████╗   
    //  ██║   ██║██╔═══╝ ██║  ██║██╔══██║   ██║   ██╔══╝   
    //  ╚██████╔╝██║     ██████╔╝██║  ██║   ██║   ███████╗ 
    //   ╚═════╝ ╚═╝     ╚═════╝ ╚═╝  ╚═╝   ╚═╝   ╚══════╝ 
    //                                                                                                                  

    public partial class Bridge
    {
        /// <summary>
        /// The main object that stores output properties over time.
        /// @TODO: each noce should have its own instance not shared across Dynamo,
        /// especially on multi-bridge listening... 
        /// </summary>
        internal static MotionUpdateOutputs motionUpdateOutputs = new MotionUpdateOutputs();


        /// <summary>
        /// Will update every time real-time motion data is received from the device.
        /// </summary>
        /// <param name="bridgeMessages">The last batch of messages received from the Bridge.</param>
        /// <param name="onlyMostRecent">If true, only the single most recent message will be output.</param>
        /// <returns name="log">Status messages.</returns>
        /// <returns name="actionTCP">Last known real-time TCP position.</returns>
        /// <returns name="actionAxes">Last known real-time robot axes.</returns>
        /// <returns name="actionExternalAxes">Last known real-time external axes.</returns>
        [MultiReturn(new[] { "log", "actionTCP", "actionAxes", "actionExternalAxes" })]
        public static Dictionary<string, object> MotionUpdate(List<string> bridgeMessages,
            bool onlyMostRecent = false)
        {

            if (bridgeMessages == null || bridgeMessages.Count == 0)
            {
                motionUpdateOutputs.ClearLog();
                motionUpdateOutputs.Log("No new messages to parse");
                return motionUpdateOutputs.GetOutputs();
            }

            motionUpdateOutputs.ParseIncomingMessages(bridgeMessages, onlyMostRecent);

            return motionUpdateOutputs.GetOutputs();
        }

    }



    //  ╔╦╗┌─┐┌┬┐┬┌─┐┌┐┌╦ ╦┌─┐┌┬┐┌─┐┌┬┐┌─┐╔═╗┬ ┬┌┬┐┌─┐┬ ┬┌┬┐┌─┐
    //  ║║║│ │ │ ││ ││││║ ║├─┘ ││├─┤ │ ├┤ ║ ║│ │ │ ├─┘│ │ │ └─┐
    //  ╩ ╩└─┘ ┴ ┴└─┘┘└┘╚═╝┴  ─┴┘┴ ┴ ┴ └─┘╚═╝└─┘ ┴ ┴  └─┘ ┴ └─┘
    //
    /// <summary>
    /// A helper class to maintain output data for this Node.
    /// </summary>
    internal class MotionUpdateOutputs
    {
        public static readonly string EVENT_NAME = "motion-update";

        public List<string> _logMsgs;
        public List<Plane> _tcps;
        public List<double?[]> _axes;
        public List<double?[]> _externalAxes;

        // Parsing stuff and buffers.
        internal static JavaScriptSerializer jsonSerializer = new JavaScriptSerializer();
        internal static List<dynamic> objBuff = new List<dynamic>();

        internal MotionUpdateOutputs()
        {
            _logMsgs = new List<string>();
            _tcps = new List<Plane>();
            _axes = new List<double?[]>();
            _externalAxes = new List<double?[]>();
        }


        /// <summary>
        /// Take a list of string json objects, check how fresh the data is, and store it if applicable. 
        /// </summary>
        /// <param name="messages"></param>
        /// <param name="keepOnlyMostRecent"></param>
        /// <returns></returns>
        public bool ParseIncomingMessages(List<string> messages, bool keepOnlyMostRecent)
        {
            objBuff.Clear();
            _logMsgs.Clear();

            dynamic json;
            var it = 0;
            foreach (var msg in messages)
            {
                try
                {
                    json = jsonSerializer.Deserialize<dynamic>(msg);

                    // Keep this message if it is of the right type and has not been received before. 
                    if (json["event"].Equals(EVENT_NAME))
                    {
                        it++;
                        objBuff.Add(json);
                    }
                }
                catch
                {
                    _logMsgs.Add("Received badly formatted message");
                }
            }

            if (objBuff.Count == 0)
            {
                _logMsgs.Add("No new messages to parse.");
                return false;
            }
            else
            {
                _logMsgs.Add("Received " + it + " messages.");
            }

            // If received any matching event not recorded before, clear all outputs, 
            // parse incoming messages and output them. 
            if (keepOnlyMostRecent)
            {
                dynamic last = objBuff.Last();
                objBuff.Clear();
                objBuff.Add(last);
            }

            ClearOutputs();
            it = 0;
            foreach (var obj in objBuff)
            {
                ParseMessage(obj);
                it++;
            }

            _logMsgs.Add("Parsed " + it + " messages.");

            return it != 0;
        }



        /// <summary>
        /// Take a json object and add addd its properties to output lists
        /// </summary>
        /// <param name="json"></param>
        /// <returns></returns>
        private bool ParseMessage(dynamic json)
        {
            try
            {
                // As opposed to GH, no similarity checks here: anything that comes in new, goes out new. 
                var pos = Machina.Utilities.Conversion.NullableDoublesFromObjects(json["pos"]);
                var ori = Machina.Utilities.Conversion.NullableDoublesFromObjects(json["ori"]);

                if (pos == null || ori == null)
                {
                    _tcps.Add(null);
                }
                else
                {
                    Plane p = Plane.ByOriginXAxisYAxis(
                        Autodesk.DesignScript.Geometry.Point.ByCoordinates(Convert.ToDouble(pos[0]),
                            Convert.ToDouble(pos[1]), Convert.ToDouble(pos[2])),
                        Autodesk.DesignScript.Geometry.Vector.ByCoordinates(Convert.ToDouble(ori[0]),
                            Convert.ToDouble(ori[1]), Convert.ToDouble(ori[2])),
                        Autodesk.DesignScript.Geometry.Vector.ByCoordinates(Convert.ToDouble(ori[3]), Convert.ToDouble(ori[4]), Convert.ToDouble(ori[5]))
                    );
                    _tcps.Add(p);
                }

                _axes.Add(Machina.Utilities.Conversion.NullableDoublesFromObjects(json["axes"]));
                _externalAxes.Add(Machina.Utilities.Conversion.NullableDoublesFromObjects(json["extax"]));
            }
            catch
            {
                _logMsgs.Add("Received badly formatted message");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Add a message to the log output.
        /// </summary>
        /// <param name="msg"></param>
        public void Log(string msg)
        {
            _logMsgs.Add(msg);
        }

        /// <summary>
        /// Clears the messages in the log output.
        /// </summary>
        public void ClearLog()
        {
            _logMsgs.Clear();
        }



        /// <summary>
        /// Clear all output lists except log messages.
        /// </summary>
        private void ClearOutputs()
        {
            _tcps.Clear();
            _axes.Clear();
            _externalAxes.Clear();
        }

        // Build the object and return it. 
        public Dictionary<string, object> GetOutputs()
        {
            return new Dictionary<string, object> {
                { "log", _logMsgs },
                { "actionTCP", _tcps},
                { "actionAxes", _axes},
                { "actionExternalAxes", _externalAxes},
            };
        }

    }
}
