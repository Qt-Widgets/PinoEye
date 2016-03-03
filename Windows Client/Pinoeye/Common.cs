using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.IO.Ports;
using System.Threading;
using Pinoeye.Attributes;
//using GMap.NET.WindowsForms;
//using GMap.NET.WindowsForms.Markers;

using System.Security.Cryptography.X509Certificates;

using System.Net;
using System.Net.Sockets;
using System.Xml; // config file
using System.Runtime.InteropServices; // dll imports

using Pinoeye;
using System.Reflection;

using System.IO;

using System.Drawing.Drawing2D;
using ProjNet.CoordinateSystems.Transformations;
using ProjNet.CoordinateSystems;

namespace Pinoeye
{

    class NoCheckCertificatePolicy : ICertificatePolicy
    {
        public bool CheckValidationResult(ServicePoint srvPoint, X509Certificate certificate, WebRequest request, int certificateProblem)
        {
            return true;
        }
    } 


    public class Common
    {
        public enum distances
        {
            Meters,
            Feet
        }

        public enum speeds
        {
            ms,
            fps,
            kph,
            mph,
            knots
        }


        /// <summary>
        /// from libraries\AP_Math\rotations.h
        /// </summary>
        public enum Rotation
        {
            ROTATION_NONE = 0,
            ROTATION_YAW_45,
            ROTATION_YAW_90,
            ROTATION_YAW_135,
            ROTATION_YAW_180,
            ROTATION_YAW_225,
            ROTATION_YAW_270,
            ROTATION_YAW_315,
            ROTATION_ROLL_180,
            ROTATION_ROLL_180_YAW_45,
            ROTATION_ROLL_180_YAW_90,
            ROTATION_ROLL_180_YAW_135,
            ROTATION_PITCH_180,
            ROTATION_ROLL_180_YAW_225,
            ROTATION_ROLL_180_YAW_270,
            ROTATION_ROLL_180_YAW_315,
            ROTATION_ROLL_90,
            ROTATION_ROLL_90_YAW_45,
            ROTATION_ROLL_90_YAW_90,
            ROTATION_ROLL_90_YAW_135,
            ROTATION_ROLL_270,
            ROTATION_ROLL_270_YAW_45,
            ROTATION_ROLL_270_YAW_90,
            ROTATION_ROLL_270_YAW_135,
            ROTATION_PITCH_90,
            ROTATION_PITCH_270,
            ROTATION_MAX
        }


        public enum ap_product
        {
            [DisplayText("HIL")]
            AP_PRODUCT_ID_NONE = 0x00,	// Hardware in the loop
            [DisplayText("APM1 1280")]
            AP_PRODUCT_ID_APM1_1280 = 0x01,// APM1 with 1280 CPUs
            [DisplayText("APM1 2560")]
            AP_PRODUCT_ID_APM1_2560 = 0x02,// APM1 with 2560 CPUs
            [DisplayText("SITL")]
            AP_PRODUCT_ID_SITL = 0x03,// Software in the loop
            [DisplayText("PX4")]
            AP_PRODUCT_ID_PX4 = 0x04,   // PX4 on NuttX
            [DisplayText("PX4 FMU 2")]
            AP_PRODUCT_ID_PX4_V2 = 0x05,   // PX4 FMU2 on NuttX
            [DisplayText("APM2 ES C4")]
            AP_PRODUCT_ID_APM2ES_REV_C4 = 0x14,// APM2 with MPU6000ES_REV_C4
            [DisplayText("APM2 ES C5")]
            AP_PRODUCT_ID_APM2ES_REV_C5 = 0x15,	// APM2 with MPU6000ES_REV_C5
            [DisplayText("APM2 ES D6")]
            AP_PRODUCT_ID_APM2ES_REV_D6 = 0x16,	// APM2 with MPU6000ES_REV_D6
            [DisplayText("APM2 ES D7")]
            AP_PRODUCT_ID_APM2ES_REV_D7 = 0x17,	// APM2 with MPU6000ES_REV_D7
            [DisplayText("APM2 ES D8")]
            AP_PRODUCT_ID_APM2ES_REV_D8 = 0x18,	// APM2 with MPU6000ES_REV_D8	
            [DisplayText("APM2 C4")]
            AP_PRODUCT_ID_APM2_REV_C4 = 0x54,// APM2 with MPU6000_REV_C4 	
            [DisplayText("APM2 C5")]
            AP_PRODUCT_ID_APM2_REV_C5 = 0x55,	// APM2 with MPU6000_REV_C5 	
            [DisplayText("APM2 D6")]
            AP_PRODUCT_ID_APM2_REV_D6 = 0x56,	// APM2 with MPU6000_REV_D6 		
            [DisplayText("APM2 D7")]
            AP_PRODUCT_ID_APM2_REV_D7 = 0x57,	// APM2 with MPU6000_REV_D7 	
            [DisplayText("APM2 D8")]
            AP_PRODUCT_ID_APM2_REV_D8 = 0x58,	// APM2 with MPU6000_REV_D8 	
            [DisplayText("APM2 D9")]
            AP_PRODUCT_ID_APM2_REV_D9 = 0x59	// APM2 with MPU6000_REV_D9 
        }
        /*
        public enum apmmodes
        {
            [DisplayText("Manual")]
            MANUAL = 0,
            [DisplayText("Circle")]
            CIRCLE = 1,
            [DisplayText("Stabilize")]
            STABILIZE = 2,
            [DisplayText("Training")]
            TRAINING = 3,
            [DisplayText("FBW A")]
            FLY_BY_WIRE_A = 5,
            [DisplayText("FBW B")]
            FLY_BY_WIRE_B = 6,
            [DisplayText("Auto")]
            AUTO = 10,
            [DisplayText("RTL")]
            RTL = 11,
            [DisplayText("Loiter")]
            LOITER = 12,
            [DisplayText("Guided")]
            GUIDED = 15,

            TAKEOFF = 99
        }

        public enum aprovermodes
        {
            [DisplayText("Manual")]
            MANUAL = 0,
            [DisplayText("Learning")]
            LEARNING = 2,
            [DisplayText("Steering")]
            STEERING = 3,
            [DisplayText("Hold")]
            HOLD = 4,
            [DisplayText("Auto")]
            AUTO = 10,
            [DisplayText("RTL")]
            RTL = 11,
            [DisplayText("Guided")]
            GUIDED = 15,
            [DisplayText("Initialising")]
            INITIALISING = 16
        }

        public enum ac2modes
        {
            [DisplayText("Stabilize")]
            STABILIZE = 0,			// hold level position
            [DisplayText("Acro")]
            ACRO = 1,			// rate control
            [DisplayText("Alt Hold")]
            ALT_HOLD = 2,		// AUTO control
            [DisplayText("Auto")]
            AUTO = 3,			// AUTO control
            [DisplayText("Guided")]
            GUIDED = 4,		// AUTO control
            [DisplayText("Loiter")]
            LOITER = 5,		// Hold a single location
            [DisplayText("RTL")]
            RTL = 6,				// AUTO control
            [DisplayText("Circle")]
            CIRCLE = 7,
            [DisplayText("Pos Hold")]
            POSITION = 8,
            [DisplayText("Land")]
            LAND = 9,				// AUTO control
            OF_LOITER = 10,
            [DisplayText("Toy")]
			TOY = 11
        }
        */
        
        public static bool getFilefromNet(string url,string saveto) {
            try
            {
                // this is for mono to a ssl server
                //ServicePointManager.CertificatePolicy = new NoCheckCertificatePolicy(); 

                ServicePointManager.ServerCertificateValidationCallback =
    new System.Net.Security.RemoteCertificateValidationCallback((sender, certificate, chain, policyErrors) => { return true; });

                // Create a request using a URL that can receive a post. 
                WebRequest request = WebRequest.Create(url);
                request.Timeout = 10000;
                // Set the Method property of the request to POST.
                request.Method = "GET";
                // Get the response.
                WebResponse response = request.GetResponse();
                // Display the status.
                if (((HttpWebResponse)response).StatusCode != HttpStatusCode.OK)
                    return false;
                // Get the stream containing content returned by the server.
                Stream dataStream = response.GetResponseStream();

                long bytes = response.ContentLength;
                long contlen = bytes;

                byte[] buf1 = new byte[1024];

                FileStream fs = new FileStream(saveto + ".new", FileMode.Create);

                DateTime dt = DateTime.Now;

                while (dataStream.CanRead && bytes > 0)
                {
                    Application.DoEvents();
                    int len = dataStream.Read(buf1, 0, buf1.Length);
                    bytes -= len;
                    fs.Write(buf1, 0, len);
                }

                fs.Close();
                dataStream.Close();
                response.Close();

                File.Delete(saveto);
                File.Move(saveto + ".new", saveto);

                return true;
            }
            catch (Exception ex) { Exception no = ex;  return false; }
        }
    }





}
