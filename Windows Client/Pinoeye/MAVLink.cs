using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Text;
using System.Runtime.InteropServices;
using System.Collections; // hashs
using System.Diagnostics; // stopwatch
using System.Reflection;
using System.Reflection.Emit;
using System.IO;
using System.Drawing;
using System.Threading;
using System.ComponentModel;
using Pinoeye.Comms;
using System.Windows.Forms;

namespace Pinoeye
{
    public class MAVLinkInterface: MAVLink, IDisposable
    {
        public ICommsSerial BaseStream { get; set; }

        public ICommsSerial MirrorStream { get; set; }

        public event EventHandler ParamListChanged;

        /// <summary>
        /// used to prevent comport access for exclusive use
        /// </summary>
        public bool giveComport { get { return _giveComport; } set { _giveComport = value; } }
        bool _giveComport = false;

        public Dictionary<string, MAV_PARAM_TYPE> param_types = new Dictionary<string, MAV_PARAM_TYPE>();

        internal string plaintxtline = "";
        string buildplaintxtline = "";

        public MAVState MAV = new MAVState();

        public class MAVState
        {
            public MAVState()
            {
                this.sysid = 0;
                this.compid = 0;
                this.param = new Hashtable();
                this.packets = new byte[0x100][];
                this.packetseencount = new int[0x100];
                this.aptype = 0;
                this.apname = 0;
                this.recvpacketcount = 0;
            }

            /// <summary>
            /// the static global state of the currently connected MAV
            /// </summary>
            public CurrentState cs = new CurrentState();
            /// <summary>
            /// mavlink remote sysid
            /// </summary>
            public byte sysid { get; set; }
            /// <summary>
            /// mavlink remove compid
            /// </summary>
            public byte compid { get; set; }
            /// <summary>
            /// storage for whole paramater list
            /// </summary>
            public Hashtable param { get; set; }
            /// <summary>
            /// storage of a previous packet recevied of a specific type
            /// </summary>
            public byte[][] packets { get; set; }
            public int[] packetseencount { get; set; }
            /// <summary>
            /// mavlink ap type
            /// </summary>
            public MAV_TYPE aptype { get; set; }
            public MAV_AUTOPILOT apname { get; set; }
            /// <summary>
            /// used as a snapshot of what is loaded on the ap atm. - derived from the stream
            /// </summary>
            public Dictionary<int, mavlink_mission_item_t> wps = new Dictionary<int, mavlink_mission_item_t>();

            public Dictionary<int, mavlink_rally_point_t> rallypoints = new Dictionary<int, mavlink_rally_point_t>();

            public Dictionary<int, mavlink_fence_point_t> fencepoints = new Dictionary<int, mavlink_fence_point_t>();

            /// <summary>
            /// Store the guided mode wp location
            /// </summary>
            public mavlink_mission_item_t GuidedMode = new mavlink_mission_item_t();

            internal int recvpacketcount = 0;
        }

        public double CONNECT_TIMEOUT_SECONDS = 30;

        /// <summary>
        /// progress form to handle connect and param requests
        /// </summary>

        /// <summary>
        /// used for outbound packet sending
        /// </summary>
        internal int packetcount = 0;
        ProgressReporterDialogue frmProgressReporter;
        /// <summary>
        /// used to calc packets per second on any single message type - used for stream rate comparaison
        /// </summary>
        public double[] packetspersecond { get; set; }
        /// <summary>
        /// time last seen a packet of a type
        /// </summary>
        DateTime[] packetspersecondbuild = new DateTime[256];


        private readonly Subject<int> _bytesReceivedSubj = new Subject<int>();
        private readonly Subject<int> _bytesSentSubj = new Subject<int>();

        /// <summary>
        /// Observable of the count of bytes received, notified when the bytes themselves are received
        /// </summary>
        public IObservable<int> BytesReceived { get { return _bytesReceivedSubj; } }

        /// <summary>
        /// Observable of the count of bytes sent, notified when the bytes themselves are received
        /// </summary>
        public IObservable<int> BytesSent { get { return _bytesSentSubj; } }

        /// <summary>
        /// Observable of the count of packets skipped (on reception), 
        /// calculated from periods where received packet sequence is not
        /// contiguous
        /// </summary>
        public Subject<int> WhenPacketLost { get; set; }

        public Subject<int> WhenPacketReceived { get; set; }

        /// <summary>
        /// used as a serial port write lock
        /// </summary>
        volatile object objlock = new object();
        /// <summary>
        /// used for a readlock on readpacket
        /// </summary>
        volatile object readlock = new object();
        /// <summary>
        /// time seen of last mavlink packet
        /// </summary>
        public DateTime lastvalidpacket { get; set; }
        /// <summary>
        /// old log support
        /// </summary>
        bool oldlogformat = false;

        /// <summary>
        /// mavlink version
        /// </summary>
        byte mavlinkversion = 0;

        /// <summary>
        /// turns on console packet display
        /// </summary>
        public bool debugmavlink { get; set; }
        /// <summary>
        /// enabled read from file mode
        /// </summary>
        public bool logreadmode { get; set; }
        public DateTime lastlogread { get; set; }
        public BinaryReader logplaybackfile { get; set; }
        public BufferedStream logfile { get; set; }
        public BufferedStream rawlogfile { get; set; }

        int bps1 = 0;
        int bps2 = 0;
        public int bps { get; set; }
        public DateTime bpstime { get; set; }

        float synclost;
        internal float packetslost = 0;
        internal float packetsnotlost = 0;
        DateTime packetlosttimer = DateTime.MinValue;

        public MAVLinkInterface()
        {
            // init fields
            //this.BaseStream = new SerialPort();
            this.packetcount = 0;

            this.packetspersecond = new double[0x100];
            this.packetspersecondbuild = new DateTime[0x100];
            this._bytesReceivedSubj = new Subject<int>();
            this._bytesSentSubj = new Subject<int>();
            this.WhenPacketLost = new Subject<int>();
            this.WhenPacketReceived = new Subject<int>();
            this.readlock = new object();
            this.lastvalidpacket = DateTime.MinValue;
            this.oldlogformat = false;
            this.mavlinkversion = 0;

            this.debugmavlink = false;
            this.logreadmode = false;
            this.lastlogread = DateTime.MinValue;
            this.logplaybackfile = null;
            this.logfile = null;
            this.rawlogfile = null;
            this.bps1 = 0;
            this.bps2 = 0;
            this.bps = 0;
            this.bpstime = DateTime.MinValue;

            this.packetslost = 0f;
            this.packetsnotlost = 0f;
            this.packetlosttimer = DateTime.MinValue;
            this.lastbad = new byte[2];

        }

        public void Close()
        {
            try
            {
                if (logfile != null)
                    logfile.Close();
            }
            catch { }
            try
            {
                if (rawlogfile != null)
                    rawlogfile.Close();
            }
            catch { }
            try
            {
                if (logplaybackfile != null)
                    logplaybackfile.Close();
            }
            catch { }

            try
            {
                if (BaseStream.IsOpen)
                    BaseStream.Close();
            }
            catch { }
        }

        public void Open()
        {
            Open(false);
        }

        public void Open(bool getparams)
        {
            if (BaseStream.IsOpen)
                return;

            frmProgressReporter = new ProgressReporterDialogue
            {
                StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
                Text = "CONNECT"
            };

            if (getparams)
            {
                frmProgressReporter.DoWork += FrmProgressReporterDoWorkAndParams;
            }
            else
            {
                frmProgressReporter.DoWork += FrmProgressReporterDoWorkNOParams;
            }
            frmProgressReporter.UpdateProgressAndStatus(-1, "연결중...");

            frmProgressReporter.RunBackgroundOperationAsync();

            if (ParamListChanged != null)
            {
                ParamListChanged(this, null);
            }
        }

        void FrmProgressReporterDoWorkAndParams(object sender, ProgressWorkerEventArgs e, object passdata = null)
        {
            OpenBg(true, e);
        }

        void FrmProgressReporterDoWorkNOParams(object sender, ProgressWorkerEventArgs e, object passdata = null)
        {
            OpenBg(false, e);
        }

        private void OpenBg(bool getparams, ProgressWorkerEventArgs progressWorkerEventArgs)
        {
            frmProgressReporter.UpdateProgressAndStatus(-1, "연결중...");

            giveComport = true;

            // allow settings to settle - previous dtr 
            System.Threading.Thread.Sleep(500);

            // reset
            MAV.sysid = 0;
            MAV.compid = 0;
            MAV.param = new Hashtable();
            MAV.packets.Initialize();

            bool hbseen = false;

            try
            {
                BaseStream.ReadBufferSize = 4 * 1024;

                lock (objlock) // so we dont have random traffic
                {

                    BaseStream.Open();

                    BaseStream.DiscardInBuffer();

                    Thread.Sleep(1000);
                }

                byte[] buffer = new byte[0];
                byte[] buffer1 = new byte[0];

                DateTime start = DateTime.Now;
                DateTime deadline = start.AddSeconds(CONNECT_TIMEOUT_SECONDS);

                var countDown = new System.Timers.Timer { Interval = 1000, AutoReset = false };
                countDown.Elapsed += (sender, e) =>
                {
                    int secondsRemaining = (deadline - e.SignalTime).Seconds;
                    //if (Progress != null)
                    //    Progress(-1, string.Format("Trying to connect.\nTimeout in {0}", secondsRemaining));
                    frmProgressReporter.UpdateProgressAndStatus(-1, string.Format("접속 시도중입니다.\n남은 시간 : {0}", secondsRemaining));
                    if (secondsRemaining > 0) countDown.Start();
                };
                countDown.Start();

                int count = 0;

                while (true)
                {
                    if (progressWorkerEventArgs.CancelRequested)
                    {
                        progressWorkerEventArgs.CancelAcknowledged = true;
                        countDown.Stop();
                        if (BaseStream.IsOpen)
                            BaseStream.Close();
                        giveComport = false;
                        return;
                    }

                    // incase we are in setup mode
                    //BaseStream.WriteLine("planner\rgcs\r");
                    if (DateTime.Now > deadline)
                    {
                        //if (Progress != null)
                        //    Progress(-1, "No Heatbeat Packets");
                        countDown.Stop();
                        this.Close();

                        if (hbseen)
                        {
                            progressWorkerEventArgs.ErrorMessage = "  접속 실패";
                            throw new Exception("");
                        }
                        else
                        {
                            progressWorkerEventArgs.ErrorMessage = "  접속 실패";
                            throw new Exception(@"");
                        }
                    }

                    System.Threading.Thread.Sleep(1);

                    // incase we are in setup mode
                    //BaseStream.WriteLine("planner\rgcs\r");

                    // can see 2 heartbeat packets at any time, and will connect - was one after the other

                    if (buffer.Length == 0)
                        buffer = getHeartBeat();

                    // incase we are in setup mode
                    //BaseStream.WriteLine("planner\rgcs\r");

                    System.Threading.Thread.Sleep(1);

                    if (buffer1.Length == 0)
                        buffer1 = getHeartBeat();


                    if (buffer.Length > 0 || buffer1.Length > 0)
                        hbseen = true;

                    count++;

                    if (buffer.Length > 5 && buffer1.Length > 5 && buffer[3] == buffer1[3] && buffer[4] == buffer1[4])
                    {
                        mavlink_heartbeat_t hb = buffer.ByteArrayToStructure<mavlink_heartbeat_t>(6);

                        if (hb.type != (byte)MAVLink.MAV_TYPE.GCS)
                        {

                            mavlinkversion = hb.mavlink_version;
                            MAV.aptype = (MAV_TYPE)hb.type;
                            MAV.apname = (MAV_AUTOPILOT)hb.autopilot;

                            MAV.sysid = buffer[3];
                            MAV.compid = buffer[4];
                            MAV.recvpacketcount = buffer[2];
                            break;
                        }
                    }

                }

                countDown.Stop();

                frmProgressReporter.UpdateProgressAndStatus(0, "제어보드 정보 불러오는 중");

                if (getparams)
                {
                    getParamListBG();
                }

                if (frmProgressReporter.doWorkArgs.CancelAcknowledged == true)
                {
                    giveComport = false;
                    if (BaseStream.IsOpen)
                        BaseStream.Close();
                    return;
                }
            }
            catch (Exception)
            {
                try
                {
                    BaseStream.Close();
                }
                catch { }
                giveComport = false;
                if (string.IsNullOrEmpty(progressWorkerEventArgs.ErrorMessage))
                    progressWorkerEventArgs.ErrorMessage = "  연결 실패";
                throw;
            }
            //frmProgressReporter.Close();
            giveComport = false;
            frmProgressReporter.UpdateProgressAndStatus(100, "연결 완료");
            packetslost = 0;
            synclost = 0;
        }
        public byte[] getHeartBeat()
        {
            DateTime start = DateTime.Now;
            int readcount = 0;
            while (true)
            {
                byte[] buffer = readPacket();
                readcount++;
                if (buffer.Length > 5)
                {
                    //log.Info("getHB packet received: " + buffer.Length + " btr " + BaseStream.BytesToRead + " type " + buffer[5] );
                    if (buffer[5] == (byte)MAVLINK_MSG_ID.HEARTBEAT)
                    {

                        return buffer;
                    }
                }
                if (DateTime.Now > start.AddMilliseconds(2200) || readcount > 200) // was 1200 , now 2.2 sec
                    return new byte[0];
            }
        }

       public void sendPacket(object indata)
        {
            byte a = 0;
            foreach (Type ty in MAVLink.MAVLINK_MESSAGE_INFO)
            {
                if (ty == indata.GetType())
                {
                    generatePacket(a, indata);
                    return;
                }
                a++;
            }
        }

        /// <summary>
        /// Generate a Mavlink Packet and write to serial
        /// </summary>
        /// <param name="messageType">type number = MAVLINK_MSG_ID</param>
        /// <param name="indata">struct of data</param>
        void generatePacket(byte messageType, object indata)
        {
            if (!BaseStream.IsOpen)
            {
                return;
            }

            lock (objlock)
            {
                byte[] data;

                if (mavlinkversion == 3)
                {
                    data = MavlinkUtil.StructureToByteArray(indata);
                }
                else
                {
                    data = MavlinkUtil.StructureToByteArrayBigEndian(indata);
                }

                //Console.WriteLine(DateTime.Now + " PC Doing req "+ messageType + " " + this.BytesToRead);
                byte[] packet = new byte[data.Length + 6 + 2];

                if (mavlinkversion == 3)
                {
                    packet[0] = 254;
                }
                else if (mavlinkversion == 2)
                {
                    packet[0] = (byte)'U';
                }
                packet[1] = (byte)data.Length;
                packet[2] = (byte)packetcount;

                packetcount++;

                packet[3] = 255; // this is always 255 - MYGCS
                packet[4] = (byte)MAV_COMPONENT.MAV_COMP_ID_Application1;
                packet[5] = messageType;

                int i = 6;
                foreach (byte b in data)
                {
                    packet[i] = b;
                    i++;
                }

                ushort checksum = MavlinkCRC.crc_calculate(packet, packet[1] + 6);

                if (mavlinkversion == 3)
                {
                    checksum = MavlinkCRC.crc_accumulate(MAVLINK_MESSAGE_CRCS[messageType], checksum);
                }

                byte ck_a = (byte)(checksum & 0xFF); ///< High byte
                byte ck_b = (byte)(checksum >> 8); ///< Low byte

                packet[i] = ck_a;
                i += 1;
                packet[i] = ck_b;
                i += 1;

                if (BaseStream.IsOpen)
                {
                    BaseStream.Write(packet, 0, i);
                    _bytesSentSubj.OnNext(i);
                }

                try
                {
                    if (logfile != null && logfile.CanWrite)
                    {
                        lock (logfile)
                        {
                            byte[] datearray = BitConverter.GetBytes((UInt64)((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds * 1000));
                            Array.Reverse(datearray);
                            logfile.Write(datearray, 0, datearray.Length);
                            logfile.Write(packet, 0, i);
                        }
                    }

                }
                catch { }
                /*
                if (messageType == (byte)MAVLink.MSG_NAMES.REQUEST_DATA_STREAM)
                {
                    try
                    {
                        BinaryWriter bw = new BinaryWriter(File.OpenWrite("serialsent.raw"));
                        bw.Seek(0, SeekOrigin.End);
                        bw.Write(packet, 0, i);
                        bw.Write((byte)'\n');
                        bw.Close();
                    }
                    catch { } // been getting errors from this. people must have it open twice.
                }*/
            }
        }

        public bool Write(string line)
        {
            lock (objlock)
            {
                BaseStream.Write(line);
            }
            _bytesSentSubj.OnNext(line.Length);
            return true;
        }

        /// <summary>
        /// set param on apm, used for param rename
        /// </summary>
        /// <param name="paramname"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool setParam(string[] paramnames, float value)
        {
            foreach (string paramname in paramnames) 
            {
                if (setParam(paramname, value))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Set parameter on apm
        /// </summary>
        /// <param name="paramname">name as a string</param>
        /// <param name="value"></param>
        public bool setParam(string paramname, float value)
        {
            if (!MAV.param.ContainsKey(paramname))
            {
                 return false;
            }

            if ((float)MAV.param[paramname] == value)
            {
                return true;
            }

            giveComport = true;

            // param type is set here, however it is always sent over the air as a float 100int = 100f.
            var req = new mavlink_param_set_t { target_system = MAV.sysid, target_component = MAV.compid, param_type = (byte)param_types[paramname] };

            byte[] temp = Encoding.ASCII.GetBytes(paramname);

            Array.Resize(ref temp, 16);
            req.param_id = temp;
            req.param_value = (value);

            generatePacket((byte)MAVLINK_MSG_ID.PARAM_SET, req);

            DateTime start = DateTime.Now;
            int retrys = 3;

            while (true)
            {
                if (!(start.AddMilliseconds(500) > DateTime.Now))
                {
                    if (retrys > 0)
                    {
                        generatePacket((byte)MAVLINK_MSG_ID.PARAM_SET, req);
                        start = DateTime.Now;
                        retrys--;
                        continue;
                    }
                    giveComport = false;
                    throw new Exception("Timeout on read - setParam " + paramname);
                }

                byte[] buffer = readPacket();
                if (buffer.Length > 5)
                {
                    if (buffer[5] == (byte)MAVLINK_MSG_ID.PARAM_VALUE)
                    {
                        mavlink_param_value_t par = buffer.ByteArrayToStructure<mavlink_param_value_t>(6);

                        string st = System.Text.ASCIIEncoding.ASCII.GetString(par.param_id);

                        int pos = st.IndexOf('\0');

                        if (pos != -1)
                        {
                            st = st.Substring(0, pos);
                        }

                        if (st != paramname)
                        {
                            continue;
                        }

                        MAV.param[st] = (par.param_value);

                        giveComport = false;
                        //System.Threading.Thread.Sleep(100);//(int)(8.5 * 5)); // 8.5ms per byte
                        return true;
                    }
                }
            }
        }

        public void getParamList()
        {

            if (ParamListChanged != null)
            {
                ParamListChanged(this, null);
            }
        }

        /// <summary>
        /// Get param list from apm
        /// </summary>
        /// <returns></returns>
        private Hashtable getParamListBG()
        {
            giveComport = true;
            List<int> indexsreceived = new List<int>();

            // clear old
            MAV.param = new Hashtable();

            int retrys = 6;
            int param_count = 0;
            int param_total = 1;

        goagain:

            mavlink_param_request_list_t req = new mavlink_param_request_list_t();
            req.target_system = MAV.sysid;
            req.target_component = MAV.compid;

            generatePacket((byte)MAVLINK_MSG_ID.PARAM_REQUEST_LIST, req);

            DateTime start = DateTime.Now;
            DateTime restart = DateTime.Now;

            DateTime lastmessage = DateTime.MinValue;

            //hires.Stopwatch stopwatch = new hires.Stopwatch();
            int packets = 0;

            do
            {
                // 4 seconds between valid packets
                if (!(start.AddMilliseconds(4000) > DateTime.Now))
                {
                    // try getting individual params
                    for (short i = 0; i <= (param_total - 1); i++)
                    {
                        if (!indexsreceived.Contains(i))
                        {
                            // prevent dropping out of this get params loop
                            try
                            {
                                GetParam(i);
                                param_count++;
                                indexsreceived.Add(i);
                            }
                            catch
                            {
                                // fail over to full list
                                break;
                            }
                        }
                    }

                    if (retrys == 4)
                    {
                        requestDatastream(MAVLink.MAV_DATA_STREAM.ALL, 1);
                    }

                    if (retrys > 0)
                    {
                        generatePacket((byte)MAVLINK_MSG_ID.PARAM_REQUEST_LIST, req);
                        start = DateTime.Now;
                        retrys--;
                        continue;
                    }
                    giveComport = false;
                    if (packets > 0 && param_total == 1)
                    {
                        throw new Exception("Timeout on read - getParamList\n" + packets + " Packets where received, but no paramater packets where received\n");
                    }
                    if (packets == 0)
                    {
                        throw new Exception("Timeout on read - getParamList\nNo Packets where received\n");
                    }

                    throw new Exception("Timeout on read - getParamList\nReceived: " + indexsreceived.Count + " of " + param_total + " after 6 retrys\n\nPlease Check\n1. Link Speed\n2. Link Quality\n3. Hardware hasn't hung");
                }

                //Console.WriteLine(DateTime.Now.Millisecond + " gp0 ");

                byte[] buffer = readPacket();
                //Console.WriteLine(DateTime.Now.Millisecond + " gp1 ");
                if (buffer.Length > 5)
                {
                    packets++;
                    // stopwatch.Start();
                    if (buffer[5] == (byte)MAVLINK_MSG_ID.PARAM_VALUE)
                    {
                        restart = DateTime.Now;
                        start = DateTime.Now;

                        mavlink_param_value_t par = buffer.ByteArrayToStructure<mavlink_param_value_t>(6);

                        // set new target
                        param_total = (par.param_count);


                        string paramID = System.Text.ASCIIEncoding.ASCII.GetString(par.param_id);

                        int pos = paramID.IndexOf('\0');
                        if (pos != -1)
                        {
                            paramID = paramID.Substring(0, pos);
                        }

                        // check if we already have it
                        if (indexsreceived.Contains(par.param_index))
                        {
                               continue;
                        }

                        //Console.WriteLine(DateTime.Now.Millisecond + " gp2 ");

                        //if (!Main.MONO)
                        //    log.Info(DateTime.Now.Millisecond + " got param " + (par.param_index) + " of " + (par.param_count) + " name: " + paramID);

                        //Console.WriteLine(DateTime.Now.Millisecond + " gp2a ");

                        MAV.param[paramID] = (par.param_value);

                        //Console.WriteLine(DateTime.Now.Millisecond + " gp2b ");

                        param_count++;
                        indexsreceived.Add(par.param_index);

                        param_types[paramID] = (MAV_PARAM_TYPE)par.param_type;

                        //Console.WriteLine(DateTime.Now.Millisecond + " gp3 ");
                        this.frmProgressReporter.UpdateProgressAndStatus((indexsreceived.Count * 100) / param_total, "APM 로딩 중");
                        // we have them all - lets escape eq total = 176 index = 0-175
                        if (par.param_index == (param_total - 1))
                            break;
                    }
                    else
                    {
                        //Console.WriteLine(DateTime.Now + " PC paramlist " + buffer[5] + " want " + MSG_NAMES.PARAM_VALUE + " btr " + BaseStream.BytesToRead);
                    }
                    //stopwatch.Stop();
                    // Console.WriteLine("Time elapsed: {0}", stopwatch.Elapsed);
                    // Console.WriteLine(DateTime.Now.Millisecond + " gp4 " + BaseStream.BytesToRead);
                }
            } while (indexsreceived.Count < param_total);

            if (indexsreceived.Count != param_total)
            {
                if (retrys > 0)
                {
                    retrys--;
                    goto goagain;
                }
                throw new Exception("Missing Params");
            }
            giveComport = false;
            return MAV.param;
        }

        public float GetParam(string name)
        {
            return GetParam(name, -1);
        }

        public float GetParam(short index)
        {
            return GetParam("", index);
        }

        /// <summary>
        /// Get param by either index or name
        /// </summary>
        /// <param name="index"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        internal float GetParam(string name = "", short index = -1)
        {
            if (name == "" && index == -1)
                return 0;

           
            giveComport = true;
            byte[] buffer;

            mavlink_param_request_read_t req = new mavlink_param_request_read_t();
            req.target_system = MAV.sysid;
            req.target_component = MAV.compid;
            req.param_index = index;
            if (index == -1)
            {
                req.param_id = System.Text.ASCIIEncoding.ASCII.GetBytes(name);
                Array.Resize(ref req.param_id, 16);
            }

            generatePacket((byte)MAVLINK_MSG_ID.PARAM_REQUEST_READ, req);

            DateTime start = DateTime.Now;
            int retrys = 3;

            while (true)
            {
                if (!(start.AddMilliseconds(200) > DateTime.Now))
                {
                    if (retrys > 0)
                    {
                        generatePacket((byte)MAVLINK_MSG_ID.PARAM_REQUEST_READ, req);
                        start = DateTime.Now;
                        retrys--;
                        continue;
                    }
                    giveComport = false;
                    throw new Exception("Timeout on read - GetParam");
                }

                buffer = readPacket();
                if (buffer.Length > 5)
                {
                    if (buffer[5] == (byte)MAVLINK_MSG_ID.PARAM_VALUE)
                    {
                        giveComport = false;

                        mavlink_param_value_t par = buffer.ByteArrayToStructure<mavlink_param_value_t>(6);

                        // not the correct id
                        if (!(par.param_index == index || ASCIIEncoding.ASCII.GetString(par.param_id) == ASCIIEncoding.ASCII.GetString(req.param_id)))
                        {
                            Console.WriteLine("Wrong Answer {0} - {1} - {2}", par.param_index, ASCIIEncoding.ASCII.GetString(par.param_id), par.param_value);
                            continue;
                        }

                        string st = System.Text.ASCIIEncoding.ASCII.GetString(par.param_id);

                        int pos = st.IndexOf('\0');

                        if (pos != -1)
                        {
                            st = st.Substring(0, pos);
                        }

                        // update table
                        MAV.param[st] = par.param_value;

                        param_types[st] = (MAV_PARAM_TYPE)par.param_type;

                       return par.param_value;
                    }
                }
            }
        }

        public static void modifyParamForDisplay(bool fromapm, string paramname, ref float value)
        {

            if (paramname.ToUpper().EndsWith("_IMAX") || paramname.ToUpper().EndsWith("ALT_HOLD_RTL") || paramname.ToUpper().EndsWith("APPROACH_ALT") || paramname.ToUpper().EndsWith("TRIM_ARSPD_CM") || paramname.ToUpper().EndsWith("MIN_GNDSPD_CM")
                || paramname.ToUpper().EndsWith("XTRK_ANGLE_CD") || paramname.ToUpper().EndsWith("LIM_PITCH_MAX") || paramname.ToUpper().EndsWith("LIM_PITCH_MIN")
                || paramname.ToUpper().EndsWith("LIM_ROLL_CD") || paramname.ToUpper().EndsWith("PITCH_MAX") || paramname.ToUpper().EndsWith("WP_SPEED_MAX"))
            {
                if (paramname.ToUpper().EndsWith("THR_RATE_IMAX") || paramname.ToUpper().EndsWith("THR_HOLD_IMAX")
                    || paramname.ToUpper().EndsWith("RATE_RLL_IMAX") || paramname.ToUpper().EndsWith("RATE_PIT_IMAX"))
                    return;

                if (fromapm)
                {
                    value /= 100.0f;
                }
                else
                {
                    value *= 100.0f;
                }
            }
            else if (paramname.ToUpper().StartsWith("TUNE_"))
            {
                if (fromapm)
                {
                    value /= 1000.0f;
                }
                else
                {
                    value *= 1000.0f;
                }
            }
        }

        /// <summary>
        /// Stops all requested data packets.
        /// </summary>
        public void stopall(bool forget)
        {
            mavlink_request_data_stream_t req = new mavlink_request_data_stream_t();
            req.target_system = MAV.sysid;
            req.target_component = MAV.compid;

            req.req_message_rate = 10;
            req.start_stop = 0; // stop
            req.req_stream_id = 0; // all

            // no error on bad
            try
            {
                generatePacket((byte)MAVLINK_MSG_ID.REQUEST_DATA_STREAM, req);
                System.Threading.Thread.Sleep(20);
                generatePacket((byte)MAVLINK_MSG_ID.REQUEST_DATA_STREAM, req);
                System.Threading.Thread.Sleep(20);
                generatePacket((byte)MAVLINK_MSG_ID.REQUEST_DATA_STREAM, req);
             }
            catch { }
        }

        public void setWPACK()
        {
            MAVLink.mavlink_mission_ack_t req = new MAVLink.mavlink_mission_ack_t();
            req.target_system = MAV.sysid;
            req.target_component = MAV.compid;
            req.type = 0;

            generatePacket((byte)MAVLINK_MSG_ID.MISSION_ACK, req);
        }

        public bool setWPCurrent(ushort index)
        {
            giveComport = true;
            byte[] buffer;

            mavlink_mission_set_current_t req = new mavlink_mission_set_current_t();

            req.target_system = MAV.sysid;
            req.target_component = MAV.compid;
            req.seq = index;

            generatePacket((byte)MAVLINK_MSG_ID.MISSION_SET_CURRENT, req);

            DateTime start = DateTime.Now;
            int retrys = 5;

            while (true)
            {
                if (!(start.AddMilliseconds(2000) > DateTime.Now))
                {
                    if (retrys > 0)
                    {
                       generatePacket((byte)MAVLINK_MSG_ID.MISSION_SET_CURRENT, req);
                        start = DateTime.Now;
                        retrys--;
                        continue;
                    }
                    giveComport = false;
                    throw new Exception("Timeout on read - setWPCurrent");
                }

                buffer = readPacket();
                if (buffer.Length > 5)
                {
                    if (buffer[5] == (byte)MAVLINK_MSG_ID.MISSION_CURRENT)
                    {
                        giveComport = false;
                        return true;
                    }
                }
            }
        }

        [Obsolete("Mavlink 09 - use doCommand", true)]
        public bool doAction(object actionid)
        {
            // mavlink 09
            throw new NotImplementedException();
        }

        /// <summary>
        /// send lots of packets to reset the hardware, this asumes we dont know the sysid in the first place
        /// </summary>
        /// <returns></returns>
        public bool doReboot(bool bootloadermode = false)
        {
            byte[] buffer = getHeartBeat();

            if (buffer.Length > 5)
            {
                mavlink_heartbeat_t hb = buffer.ByteArrayToStructure<mavlink_heartbeat_t>(6);

                mavlinkversion = hb.mavlink_version;
                MAV.aptype = (MAV_TYPE)hb.type;
                MAV.apname = (MAV_AUTOPILOT)hb.autopilot;
                MAV.sysid = buffer[3];
                MAV.compid = buffer[4];

            }

            int param1 = 1;
            if (bootloadermode)
            {
                param1 = 3;
            }

            if (MAV.sysid != 0 && MAV.compid != 0)
            {
                doCommand(MAV_CMD.PREFLIGHT_REBOOT_SHUTDOWN, param1, 0, 0, 0, 0, 0, 0);
                doCommand(MAV_CMD.PREFLIGHT_REBOOT_SHUTDOWN, 1, 0, 0, 0, 0, 0, 0);
            }
            else
            {

                for (byte a = byte.MinValue; a < byte.MaxValue; a++)
                {
                    giveComport = true;
                    MAV.sysid = a;
                    doCommand(MAV_CMD.PREFLIGHT_REBOOT_SHUTDOWN, param1, 0, 0, 0, 0, 0, 0);
                }
            }
            giveComport = false;
            return true;
        }

        public bool doARM(bool armit)
        {
            return doCommand(MAV_CMD.COMPONENT_ARM_DISARM, armit ? 1 : 0, 0, 0, 0, 0, 0, 0);
        }

        public bool doCommand(MAV_CMD actionid, float p1, float p2, float p3, float p4, float p5, float p6, float p7)
        {

            giveComport = true;
            byte[] buffer;

            mavlink_command_long_t req = new mavlink_command_long_t();

            req.target_system = MAV.sysid;
            req.target_component = MAV.compid;

            if (actionid == MAV_CMD.COMPONENT_ARM_DISARM)
            {
                req.target_component = (byte)MAV_COMPONENT.MAV_COMP_ID_SYSTEM_CONTROL;
            }

            req.command = (ushort)actionid;

            req.param1 = p1;
            req.param2 = p2;
            req.param3 = p3;
            req.param4 = p4;
            req.param5 = p5;
            req.param6 = p6;
            req.param7 = p7;

            generatePacket((byte)MAVLINK_MSG_ID.COMMAND_LONG, req);

            DateTime start = DateTime.Now;
            int retrys = 3;

            int timeout = 2000;

            // imu calib take a little while
            if (actionid == MAV_CMD.PREFLIGHT_CALIBRATION && p5 == 1)
            {
                // this is for advanced accel offsets, and blocks execution
                return true;
            }
            else if (actionid == MAV_CMD.PREFLIGHT_CALIBRATION)
            {
                retrys = 1;
                timeout = 25000;
            }
            else if (actionid == MAV_CMD.PREFLIGHT_REBOOT_SHUTDOWN)
            {
                generatePacket((byte)MAVLINK_MSG_ID.COMMAND_LONG, req);
                giveComport = false;
                return true;
            }
            else if (actionid == MAV_CMD.COMPONENT_ARM_DISARM)
            {
                // 10 seconds as may need an imu calib
                timeout = 10000;
            }

            while (true)
            {
                if (!(start.AddMilliseconds(timeout) > DateTime.Now))
                {
                    if (retrys > 0)
                    {
                        generatePacket((byte)MAVLINK_MSG_ID.COMMAND_LONG, req);
                        start = DateTime.Now;
                        retrys--;
                        continue;
                    }
                    giveComport = false;
                    throw new Exception("Timeout on read - doAction");
                }

                buffer = readPacket();
                if (buffer.Length > 5)
                {
                    if (buffer[5] == (byte)MAVLINK_MSG_ID.COMMAND_ACK)
                    {


                        var ack = buffer.ByteArrayToStructure<mavlink_command_ack_t>(6);


                        if (ack.result == (byte)MAV_RESULT.ACCEPTED)
                        {
                            giveComport = false;
                            return true;
                        }
                        else
                        {
                            giveComport = false;
                            return false;
                        }
                    }
                }
            }
        }

        public void requestDatastream(MAVLink.MAV_DATA_STREAM id, byte hzrate)
        {

            double pps = 0;

            switch (id)
            {
                case MAVLink.MAV_DATA_STREAM.ALL:

                    break;
                case MAVLink.MAV_DATA_STREAM.EXTENDED_STATUS:
                    if (packetspersecondbuild[(byte)MAVLINK_MSG_ID.SYS_STATUS] < DateTime.Now.AddSeconds(-2))
                        break;
                    pps = packetspersecond[(byte)MAVLINK_MSG_ID.SYS_STATUS];
                    if (hzratecheck(pps, hzrate))
                    {
                        return;
                    }
                    break;
                case MAVLink.MAV_DATA_STREAM.EXTRA1:
                    if (packetspersecondbuild[(byte)MAVLINK_MSG_ID.ATTITUDE] < DateTime.Now.AddSeconds(-2))
                        break;
                    pps = packetspersecond[(byte)MAVLINK_MSG_ID.ATTITUDE];
                    if (hzratecheck(pps, hzrate))
                    {
                        return;
                    }
                    break;
                case MAVLink.MAV_DATA_STREAM.EXTRA2:
                    if (packetspersecondbuild[(byte)MAVLINK_MSG_ID.VFR_HUD] < DateTime.Now.AddSeconds(-2))
                        break;
                    pps = packetspersecond[(byte)MAVLINK_MSG_ID.VFR_HUD];
                    if (hzratecheck(pps, hzrate))
                    {
                        return;
                    }
                    break;
                case MAVLink.MAV_DATA_STREAM.EXTRA3:
                    if (packetspersecondbuild[(byte)MAVLINK_MSG_ID.AHRS] < DateTime.Now.AddSeconds(-2))
                        break;
                    pps = packetspersecond[(byte)MAVLINK_MSG_ID.AHRS];
                    if (hzratecheck(pps, hzrate))
                    {
                        return;
                    }
                    break;
                case MAVLink.MAV_DATA_STREAM.POSITION:
                    if (packetspersecondbuild[(byte)MAVLINK_MSG_ID.GLOBAL_POSITION_INT] < DateTime.Now.AddSeconds(-2))
                        break;
                    pps = packetspersecond[(byte)MAVLINK_MSG_ID.GLOBAL_POSITION_INT];
                    if (hzratecheck(pps, hzrate))
                    {
                        return;
                    }
                    break;
                case MAVLink.MAV_DATA_STREAM.RAW_CONTROLLER:
                    if (packetspersecondbuild[(byte)MAVLINK_MSG_ID.RC_CHANNELS_SCALED] < DateTime.Now.AddSeconds(-2))
                        break;
                    pps = packetspersecond[(byte)MAVLINK_MSG_ID.RC_CHANNELS_SCALED];
                    if (hzratecheck(pps, hzrate))
                    {
                        return;
                    }
                    break;
                case MAVLink.MAV_DATA_STREAM.RAW_SENSORS:
                    if (packetspersecondbuild[(byte)MAVLINK_MSG_ID.RAW_IMU] < DateTime.Now.AddSeconds(-2))
                        break;
                    pps = packetspersecond[(byte)MAVLINK_MSG_ID.RAW_IMU];
                    if (hzratecheck(pps, hzrate))
                    {
                        return;
                    }
                    break;
                case MAVLink.MAV_DATA_STREAM.RC_CHANNELS:
                    if (packetspersecondbuild[(byte)MAVLINK_MSG_ID.RC_CHANNELS_RAW] < DateTime.Now.AddSeconds(-2))
                        break;
                    pps = packetspersecond[(byte)MAVLINK_MSG_ID.RC_CHANNELS_RAW];
                    if (hzratecheck(pps, hzrate))
                    {
                        return;
                    }
                    break;
            }

            //packetspersecond[temp[5]];

            if (pps == 0 && hzrate == 0)
            {
                return;
            }


            getDatastream(id, hzrate);
        }

        // returns true for ok
        bool hzratecheck(double pps, int hzrate)
        {

            if (hzrate == 0 && pps == 0)
            {
                return true;
            }
            else if (hzrate == 1 && pps >= 0.5 && pps <= 2)
            {
                return true;
            }
            else if (hzrate == 3 && pps >= 2 && hzrate < 5)
            {
                return true;
            }
            else if (hzrate == 10 && pps > 5 && hzrate < 15)
            {
                return true;
            }
            else if (hzrate > 15 && pps > 15)
            {
                return true;
            }

            return false;

        }

        void getDatastream(MAVLink.MAV_DATA_STREAM id, byte hzrate)
        {
            mavlink_request_data_stream_t req = new mavlink_request_data_stream_t();
            req.target_system = MAV.sysid;
            req.target_component = MAV.compid;

            req.req_message_rate = hzrate;
            req.start_stop = 1; // start
            req.req_stream_id = (byte)id; // id

            // send each one twice.
            generatePacket((byte)MAVLINK_MSG_ID.REQUEST_DATA_STREAM, req);
            generatePacket((byte)MAVLINK_MSG_ID.REQUEST_DATA_STREAM, req);
        }

        /// <summary>
        /// Returns WP count
        /// </summary>
        /// <returns></returns>
        public byte getWPCount()
        {
            giveComport = true;
            byte[] buffer;
            mavlink_mission_request_list_t req = new mavlink_mission_request_list_t();

            req.target_system = MAV.sysid;
            req.target_component = MAV.compid;

            // request list
            generatePacket((byte)MAVLINK_MSG_ID.MISSION_REQUEST_LIST, req);

            DateTime start = DateTime.Now;
            int retrys = 6;

            while (true)
            {
                if (!(start.AddMilliseconds(500) > DateTime.Now))
                {
                    if (retrys > 0)
                    {
                        generatePacket((byte)MAVLINK_MSG_ID.MISSION_REQUEST_LIST, req);
                        start = DateTime.Now;
                        retrys--;
                        continue;
                    }
                    giveComport = false;
                    //return (byte)int.Parse(param["WP_TOTAL"].ToString());
                    throw new Exception("Timeout on read - getWPCount");
                }

                buffer = readPacket();
                if (buffer.Length > 5)
                {
                    if (buffer[5] == (byte)MAVLINK_MSG_ID.MISSION_COUNT)
                    {



                        var count = buffer.ByteArrayToStructure<mavlink_mission_count_t>(6);


                        giveComport = false;
                        return (byte)count.count; // should be ushort, but apm has limited wp count < byte
                    }
                }
            }
        }
        /// <summary>
        /// Gets specfied WP
        /// </summary>
        /// <param name="index"></param>
        /// <returns>WP</returns>
 
        public object DebugPacket(byte[] datin)
        {
            string text = "";
            return DebugPacket(datin, ref text, true);
        }

        public object DebugPacket(byte[] datin, bool PrintToConsole)
        {
            string text = "";
            return DebugPacket(datin, ref text, PrintToConsole);
        }

        public object DebugPacket(byte[] datin, ref string text)
        {
            return DebugPacket(datin, ref text, true);
        }

        /// <summary>
        /// Print entire decoded packet to console
        /// </summary>
        /// <param name="datin">packet byte array</param>
        /// <returns>struct of data</returns>
        public object DebugPacket(byte[] datin, ref string text, bool PrintToConsole, string delimeter = " ")
        {
            string textoutput;
            try
            {
                if (datin.Length > 5)
                {
                    byte header = datin[0];
                    byte length = datin[1];
                    byte seq = datin[2];
                    byte sysid = datin[3];
                    byte compid = datin[4];
                    byte messid = datin[5];

                    textoutput = string.Format("{0,2:X}{6}{1,2:X}{6}{2,2:X}{6}{3,2:X}{6}{4,2:X}{6}{5,2:X}{6}", header, length, seq, sysid, compid, messid, delimeter);

                    object data = Activator.CreateInstance(MAVLINK_MESSAGE_INFO[messid]);

                    MavlinkUtil.ByteArrayToStructure(datin, ref data, 6);

                    Type test = data.GetType();

                    if (PrintToConsole)
                    {

                        textoutput = textoutput + test.Name + delimeter;

                        foreach (var field in test.GetFields())
                        {
                            // field.Name has the field's name.

                            object fieldValue = field.GetValue(data); // Get value

                            if (field.FieldType.IsArray)
                            {
                                textoutput = textoutput + field.Name + delimeter;
                                byte[] crap = (byte[])fieldValue;
                                foreach (byte fiel in crap)
                                {
                                    if (fiel == 0)
                                    {
                                        break;
                                    }
                                    else
                                    {
                                        textoutput = textoutput + (char)fiel;
                                    }
                                }
                                textoutput = textoutput + delimeter;
                            }
                            else
                            {
                                textoutput = textoutput + field.Name + delimeter + fieldValue.ToString() + delimeter;
                            }
                        }
                        textoutput = textoutput + delimeter + "Len" + delimeter + datin.Length + "\r\n";
                        if (PrintToConsole)
                            Console.Write(textoutput);

                        if (text != null)
                            text = textoutput;
                    }

                    return data;
                }
            }
            catch { }

            return null;
        }

        public object GetPacket(byte[] datin)
        {
            if (datin.Length > 5)
            {
                byte header = datin[0];
                byte length = datin[1];
                byte seq = datin[2];
                byte sysid = datin[3];
                byte compid = datin[4];
                byte messid = datin[5];

                try
                {
                    object data = Activator.CreateInstance(MAVLINK_MESSAGE_INFO[messid]);

                    MavlinkUtil.ByteArrayToStructure(datin, ref data, 6);

                    return data;
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Sets wp total count
        /// </summary>
        /// <param name="wp_total"></param>
        public void setWPTotal(ushort wp_total)
        {
            giveComport = true;
            mavlink_mission_count_t req = new mavlink_mission_count_t();

            req.target_system = MAV.sysid;
            req.target_component = MAV.compid; // MSG_NAMES.MISSION_COUNT

            req.count = wp_total;

            generatePacket((byte)MAVLINK_MSG_ID.MISSION_COUNT, req);

            DateTime start = DateTime.Now;
            int retrys = 3;

            while (true)
            {
                if (!(start.AddMilliseconds(700) > DateTime.Now))
                {
                    if (retrys > 0)
                    {
                        generatePacket((byte)MAVLINK_MSG_ID.MISSION_COUNT, req);
                        start = DateTime.Now;
                        retrys--;
                        continue;
                    }
                    giveComport = false;
                    throw new Exception("Timeout on read - setWPTotal");
                }
                byte[] buffer = readPacket();
                if (buffer.Length > 9)
                {
                    if (buffer[5] == (byte)MAVLINK_MSG_ID.MISSION_REQUEST)
                    {
                        var request = buffer.ByteArrayToStructure<mavlink_mission_request_t>(6);

                        if (request.seq == 0)
                        {
                            if (MAV.param["WP_TOTAL"] != null)
                                MAV.param["WP_TOTAL"] = (float)wp_total - 1;
                            if (MAV.param["CMD_TOTAL"] != null)
                                MAV.param["CMD_TOTAL"] = (float)wp_total - 1;

                            MAV.wps.Clear();

                            giveComport = false;
                            return;
                        }
                    }
                    else
                    {
                        //Console.WriteLine(DateTime.Now + " PC getwp " + buffer[5]);
                    }
                }
            }
        }

        /// <summary>
        /// Save wp to eeprom
        /// </summary>
        /// <param name="loc">location struct</param>
        /// <param name="index">wp no</param>
        /// <param name="frame">global or relative</param>
        /// <param name="current">0 = no , 2 = guided mode</param>
  
        public void setDigicamConfigure()
        {
            // not implmented
        }

        public void setDigicamControl(bool shot)
        {
            mavlink_digicam_control_t req = new mavlink_digicam_control_t();

            req.target_system = MAV.sysid;
            req.target_component = MAV.compid;
            req.shot = (shot == true) ? (byte)1 : (byte)0;

            generatePacket((byte)MAVLINK_MSG_ID.DIGICAM_CONTROL, req);
            System.Threading.Thread.Sleep(20);
            generatePacket((byte)MAVLINK_MSG_ID.DIGICAM_CONTROL, req);
        }

        public void setMountConfigure(MAV_MOUNT_MODE mountmode, bool stabroll, bool stabpitch, bool stabyaw)
        {
            mavlink_mount_configure_t req = new mavlink_mount_configure_t();

            req.target_system = MAV.sysid;
            req.target_component = MAV.compid;
            req.mount_mode = (byte)mountmode;
            req.stab_pitch = (stabpitch == true) ? (byte)1 : (byte)0;
            req.stab_roll = (stabroll == true) ? (byte)1 : (byte)0;
            req.stab_yaw = (stabyaw == true) ? (byte)1 : (byte)0;

            generatePacket((byte)MAVLINK_MSG_ID.MOUNT_CONFIGURE, req);
            System.Threading.Thread.Sleep(20);
            generatePacket((byte)MAVLINK_MSG_ID.MOUNT_CONFIGURE, req);
        }

        public void setMountControl(double pa, double pb, double pc, bool islatlng)
        {
            mavlink_mount_control_t req = new mavlink_mount_control_t();

            req.target_system = MAV.sysid;
            req.target_component = MAV.compid;
            if (!islatlng)
            {
                req.input_a = (int)pa;
                req.input_b = (int)pb;
                req.input_c = (int)pc;
            }
            else
            {
                req.input_a = (int)(pa * 10000000.0);
                req.input_b = (int)(pb * 10000000.0);
                req.input_c = (int)(pc * 100.0);
            }

            generatePacket((byte)MAVLINK_MSG_ID.MOUNT_CONTROL, req);
            System.Threading.Thread.Sleep(20);
            generatePacket((byte)MAVLINK_MSG_ID.MOUNT_CONTROL, req);
        }

        public void setMode(string modein)
        {
            try
            {
                MAVLink.mavlink_set_mode_t mode = new MAVLink.mavlink_set_mode_t();

                if (translateMode(modein, ref mode))
                {
                    setMode(mode);
                }
            }
            catch { System.Windows.Forms.MessageBox.Show("Failed to change Modes"); }
        }

        public void setMode(mavlink_set_mode_t mode, MAV_MODE_FLAG base_mode = 0)
        {
            mode.base_mode |= (byte)base_mode;

            generatePacket((byte)(byte)MAVLink.MAVLINK_MSG_ID.SET_MODE, mode);
            System.Threading.Thread.Sleep(10);
            generatePacket((byte)(byte)MAVLink.MAVLINK_MSG_ID.SET_MODE, mode);
        }

        /// <summary>
        /// used for last bad serial characters
        /// </summary>
        byte[] lastbad = new byte[2];

        /// <summary>
        /// Serial Reader to read mavlink packets. POLL method
        /// </summary>
        /// <returns></returns>
        public byte[] readPacket()
        {
            byte[] buffer = new byte[260];
            int count = 0;
            int length = 0;
            int readcount = 0;
            lastbad = new byte[2];

            BaseStream.ReadTimeout = 1200; // 1200 ms between chars - the gps detection requires this.

            DateTime start = DateTime.Now;

            //Console.WriteLine(DateTime.Now.Millisecond + " SR0 " + BaseStream.BytesToRead);

            lock (readlock)
            {
                //Console.WriteLine(DateTime.Now.Millisecond + " SR1 " + BaseStream.BytesToRead);

                while (BaseStream.IsOpen || logreadmode)
                {
                    try
                    {
                        if (readcount > 300)
                        {
                            break;
                        }
                        readcount++;
                        if (logreadmode)
                        {
                            try
                            {
                                if (logplaybackfile.BaseStream.Position == 0)
                                {
                                    if (logplaybackfile.PeekChar() == '-')
                                    {
                                        oldlogformat = true;
                                    }
                                    else
                                    {
                                        oldlogformat = false;
                                    }
                                }
                            }
                            catch { oldlogformat = false; }

                            if (oldlogformat)
                            {
                                buffer = readlogPacket(); //old style log
                            }
                            else
                            {
                                buffer = readlogPacketMavlink();
                            }
                        }
                        else
                        {
                            MAV.cs.datetime = DateTime.Now;

                            DateTime to = DateTime.Now.AddMilliseconds(BaseStream.ReadTimeout);

                            // Console.WriteLine(DateTime.Now.Millisecond + " SR1a " + BaseStream.BytesToRead);

                            while (BaseStream.IsOpen && BaseStream.BytesToRead <= 0)
                            {
                                if (DateTime.Now > to)
                                {
                                    throw new Exception("Timeout");
                                }
                                System.Threading.Thread.Sleep(1);
                                //Console.WriteLine(DateTime.Now.Millisecond + " SR0b " + BaseStream.BytesToRead);
                            }
                            //Console.WriteLine(DateTime.Now.Millisecond + " SR1a " + BaseStream.BytesToRead);
                            if (BaseStream.IsOpen)
                            {
                                BaseStream.Read(buffer, count, 1);
                                if (rawlogfile != null && rawlogfile.CanWrite)
                                    rawlogfile.WriteByte(buffer[count]);
                            }
                            //Console.WriteLine(DateTime.Now.Millisecond + " SR1b " + BaseStream.BytesToRead);
                        }
                    }
                    catch (Exception e) { Exception no = e;  break; }

                    // check if looks like a mavlink packet and check for exclusions and write to console
                    if (buffer[0] != 254)
                    {
                        if (buffer[0] >= 0x20 && buffer[0] <= 127 || buffer[0] == '\n' || buffer[0] == '\r')
                        {
                            // check for line termination
                            if (buffer[0] == '\r' || buffer[0] == '\n')
                            {
                                // check new line is valid
                                if (buildplaintxtline.Length > 3)
                                    plaintxtline = buildplaintxtline;

                                // reset for next line
                                buildplaintxtline = "";
                            }

                            //TCPConsole.Write(buffer[0]);
                            Console.Write((char)buffer[0]);
                            buildplaintxtline += (char)buffer[0];
                        }
                        _bytesReceivedSubj.OnNext(1);
                        count = 0;
                        lastbad[0] = lastbad[1];
                        lastbad[1] = buffer[0];
                        buffer[1] = 0;
                        continue;
                    }
                    // reset count on valid packet
                    readcount = 0;

                    //Console.WriteLine(DateTime.Now.Millisecond + " SR2 " + BaseStream.BytesToRead);

                    // check for a header
                    if (buffer[0] == 254)
                    {
                        // if we have the header, and no other chars, get the length and packet identifiers
                        if (count == 0 && !logreadmode)
                        {
                            DateTime to = DateTime.Now.AddMilliseconds(BaseStream.ReadTimeout);

                            while (BaseStream.IsOpen && BaseStream.BytesToRead < 5)
                            {
                                if (DateTime.Now > to)
                                {
                                   throw new Exception("Timeout");
                                }
                                System.Threading.Thread.Sleep(1);
                                //Console.WriteLine(DateTime.Now.Millisecond + " SR0b " + BaseStream.BytesToRead);
                            }
                            int read = BaseStream.Read(buffer, 1, 5);
                            count = read;
                            if (rawlogfile != null && rawlogfile.CanWrite)
                                rawlogfile.Write(buffer, 1, read);
                        }

                        // packet length
                        length = buffer[1] + 6 + 2 - 2; // data + header + checksum - U - length
                        if (count >= 5 || logreadmode)
                        {
                            if (MAV.sysid != 0)
                            {
                                if (MAV.sysid != buffer[3] || MAV.compid != buffer[4])
                                {
                                    if (buffer[3] == '3' && buffer[4] == 'D')
                                    {
                                        // this is a 3dr radio rssi packet
                                    }
                                    else
                                    {
                                        return new byte[0];
                                    }
                                }
                            }

                            try
                            {
                                if (logreadmode)
                                {

                                }
                                else
                                {
                                    DateTime to = DateTime.Now.AddMilliseconds(BaseStream.ReadTimeout);

                                    while (BaseStream.IsOpen && BaseStream.BytesToRead < (length - 4))
                                    {
                                        if (DateTime.Now > to)
                                        {
                                             break;
                                        }
                                        System.Threading.Thread.Sleep(1);
                                    }
                                    if (BaseStream.IsOpen)
                                    {
                                        int read = BaseStream.Read(buffer, 6, length - 4);
                                        if (rawlogfile != null && rawlogfile.CanWrite)
                                        {
                                            // write only what we read, temp is the whole packet, so 6-end
                                            rawlogfile.Write(buffer, 6, read);
                                        }
                                    }
                                }
                                count = length + 2;
                            }
                            catch { break; }
                            break;
                        }
                    }

                    count++;
                    if (count == 299)
                        break;
                }

                //Console.WriteLine(DateTime.Now.Millisecond + " SR3 " + BaseStream.BytesToRead);
            }// end readlock

            Array.Resize<byte>(ref buffer, count);

            _bytesReceivedSubj.OnNext(buffer.Length);

            if (!logreadmode && packetlosttimer.AddSeconds(5) < DateTime.Now)
            {
                packetlosttimer = DateTime.Now;
                packetslost = (packetslost * 0.8f);
                packetsnotlost = (packetsnotlost * 0.8f);
            }
            else if (logreadmode && packetlosttimer.AddSeconds(5) < lastlogread)
            {
                packetlosttimer = lastlogread;
                packetslost = (packetslost * 0.8f);
                packetsnotlost = (packetsnotlost * 0.8f);
            }

            //MAV.cs.linkqualitygcs = (ushort)((packetsnotlost / (packetsnotlost + packetslost)) * 100.0);

            if (bpstime.Second != DateTime.Now.Second && !logreadmode && BaseStream.IsOpen)
            {
                Console.Write("bps {0} loss {1} left {2} mem {3}      \n", bps1, synclost, BaseStream.BytesToRead, System.GC.GetTotalMemory(false) / 1024 / 1024.0);
                bps2 = bps1; // prev sec
                bps1 = 0; // current sec
                bpstime = DateTime.Now;
            }

            bps1 += buffer.Length;

            bps = (bps1 + bps2) / 2;

            if (buffer.Length >= 5 && (buffer[3] == 255 || buffer[3] == 253) && logreadmode) // gcs packet
            {
                getWPsfromstream(ref buffer);
                return buffer;// new byte[0];
            }

            ushort crc = MavlinkCRC.crc_calculate(buffer, buffer.Length - 2);

            if (buffer.Length > 5 && buffer[0] == 254)
            {
                crc = MavlinkCRC.crc_accumulate(MAVLINK_MESSAGE_CRCS[buffer[5]], crc);
            }

            if (buffer.Length > 5 && buffer[1] != MAVLINK_MESSAGE_LENGTHS[buffer[5]])
            {
                if (MAVLINK_MESSAGE_LENGTHS[buffer[5]] == 0) // pass for unknown packets
                {

                }
                else
                {
                    if (buffer.Length == 11 && buffer[0] == 'U' && buffer[5] == 0)
                    {
                        string message = "Mavlink 0.9 Heartbeat, Please upgrade your AP, This planner is for Mavlink 1.0\n\n";
                        throw new Exception(message);
                    }
                    return new byte[0];
                }
            }

            if (buffer.Length < 5 || buffer[buffer.Length - 1] != (crc >> 8) || buffer[buffer.Length - 2] != (crc & 0xff))
            {
                int packetno = -1;
                if (buffer.Length > 5)
                {
                    packetno = buffer[5];
                }
                return new byte[0];
            }

            try
            {
                if ((buffer[0] == 'U' || buffer[0] == 254) && buffer.Length >= buffer[1])
                {
                    if (buffer[3] == '3' && buffer[4] == 'D')
                    {

                    }
                    else
                    {


                        byte packetSeqNo = buffer[2];
                        int expectedPacketSeqNo = ((MAV.recvpacketcount + 1) % 0x100);

                        {
                            if (packetSeqNo != expectedPacketSeqNo)
                            {
                                synclost++; // actualy sync loss's
                                int numLost = 0;

                                if (packetSeqNo < ((MAV.recvpacketcount + 1))) // recvpacketcount = 255 then   10 < 256 = true if was % 0x100 this would fail
                                {
                                    numLost = 0x100 - expectedPacketSeqNo + packetSeqNo;
                                }
                                else
                                {
                                    numLost = packetSeqNo - MAV.recvpacketcount;
                                }
                                packetslost += numLost;
                                WhenPacketLost.OnNext(numLost);

                                  }

                            packetsnotlost++;

                            MAV.recvpacketcount = packetSeqNo;
                        }
                        WhenPacketReceived.OnNext(1);
                        // Console.WriteLine(DateTime.Now.Millisecond);
                    }

                    //                    Console.Write(temp[5] + " " + DateTime.Now.Millisecond + " " + packetspersecond[temp[5]] + " " + (DateTime.Now - packetspersecondbuild[temp[5]]).TotalMilliseconds + "     \n");

                    if (double.IsInfinity(packetspersecond[buffer[5]]))
                        packetspersecond[buffer[5]] = 0;

                    packetspersecond[buffer[5]] = (((1000 / ((DateTime.Now - packetspersecondbuild[buffer[5]]).TotalMilliseconds) + packetspersecond[buffer[5]]) / 2));

                    packetspersecondbuild[buffer[5]] = DateTime.Now;

                    //Console.WriteLine("Packet {0}",temp[5]);
                    // store packet history
                    lock (objlock)
                    {
                        MAV.packets[buffer[5]] = buffer;
                        MAV.packetseencount[buffer[5]]++;
                    }

                    if (debugmavlink)
                        DebugPacket(buffer);

                    if (buffer[5] == (byte)MAVLink.MAVLINK_MSG_ID.STATUSTEXT) // status text
                    {
                        string logdata = Encoding.ASCII.GetString(buffer, 7, buffer.Length - 7);
                        int ind = logdata.IndexOf('\0');
                        if (ind != -1)
                            logdata = logdata.Substring(0, ind);
                        /*
                        if (Main.speechEngine != null && Main.config["speechenable"] != null && Main.config["speechenable"].ToString() == "True")
                        {
                            //Main.talk.SpeakAsync(logdata);
                        }
                        */
                    }

                    // set ap type
                    if (buffer[5] == (byte)MAVLink.MAVLINK_MSG_ID.HEARTBEAT)
                    {
                        mavlink_heartbeat_t hb = buffer.ByteArrayToStructure<mavlink_heartbeat_t>(6);

                        if (hb.type != (byte)MAVLink.MAV_TYPE.GCS)
                        {
                            mavlinkversion = hb.mavlink_version;
                            MAV.aptype = (MAV_TYPE)hb.type;
                            MAV.apname = (MAV_AUTOPILOT)hb.autopilot;
                        }
                    }

                    getWPsfromstream(ref buffer);

                    try
                    {
                        if (logfile != null && logfile.CanWrite && !logreadmode)
                        {
                            lock (logfile)
                            {
                                byte[] datearray = BitConverter.GetBytes((UInt64)((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds * 1000));
                                Array.Reverse(datearray);
                                logfile.Write(datearray, 0, datearray.Length);
                                logfile.Write(buffer, 0, buffer.Length);

                                if (buffer[5] == 0)
                                {// flush on heartbeat - 1 seconds
                                    logfile.Flush();
                                    rawlogfile.Flush();
                                }
                            }
                        }

                    }
                    catch { }

                    try
                    {
                        // full rw from mirror stream
                        if (MirrorStream != null && MirrorStream.IsOpen)
                        {
                            MirrorStream.Write(buffer, 0, buffer.Length);

                            while (MirrorStream.BytesToRead > 0)
                            {
                                byte[] buf = new byte[1024];

                                int len = MirrorStream.Read(buf, 0, buf.Length);

                                BaseStream.Write(buf, 0, len);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            if (buffer[3] == '3' && buffer[4] == 'D')
            {
                // dont update last packet time for 3dr radio packets
            }
            else
            {
                lastvalidpacket = DateTime.Now;
            }

            //            Console.Write((DateTime.Now - start).TotalMilliseconds.ToString("00.000") + "\t" + temp.Length + "     \r");

            //   Console.WriteLine(DateTime.Now.Millisecond + " SR4 " + BaseStream.BytesToRead);

            return buffer;
        }

        /// <summary>
        /// Used to extract mission from log file - both sent or received
        /// </summary>
        /// <param name="buffer">packet</param>
        void getWPsfromstream(ref byte[] buffer)
        {
            if (buffer[5] == (byte)MAVLINK_MSG_ID.MISSION_COUNT)
            {
                // clear old
                MAV.wps.Clear();
            }

            if (buffer[5] == (byte)MAVLink.MAVLINK_MSG_ID.MISSION_ITEM)
            {
                mavlink_mission_item_t wp = buffer.ByteArrayToStructure<mavlink_mission_item_t>(6);

                if (wp.current == 2)
                {
                    // guide mode wp
                    MAV.GuidedMode = wp;
                }
                else
                {
                    MAV.wps[wp.seq] = wp;
                }

                Console.WriteLine("WP # {7} cmd {8} p1 {0} p2 {1} p3 {2} p4 {3} x {4} y {5} z {6}", wp.param1, wp.param2, wp.param3, wp.param4, wp.x, wp.y, wp.z, wp.seq, wp.command);
            }

            if (buffer[5] == (byte)MAVLINK_MSG_ID.RALLY_POINT)
            {
                mavlink_rally_point_t rallypt = buffer.ByteArrayToStructure<mavlink_rally_point_t>(6);

                MAV.rallypoints[rallypt.idx] = rallypt;

                Console.WriteLine("RP # {0} {1} {2} {3} {4}", rallypt.idx, rallypt.lat,rallypt.lng,rallypt.alt, rallypt.break_alt);
            }

            if (buffer[5] == (byte)MAVLINK_MSG_ID.FENCE_POINT)
            {
                mavlink_fence_point_t fencept = buffer.ByteArrayToStructure<mavlink_fence_point_t>(6);

                MAV.fencepoints[fencept.idx] = fencept;
            }
        }

   
        byte[] readlogPacket()
        {
            byte[] temp = new byte[300];

            MAV.sysid = 0;

            int a = 0;
            while (a < temp.Length && logplaybackfile.BaseStream.Position != logplaybackfile.BaseStream.Length)
            {
                temp[a] = (byte)logplaybackfile.BaseStream.ReadByte();
                //Console.Write((char)temp[a]);
                if (temp[a] == ':')
                {
                    break;
                }
                a++;
                if (temp[0] != '-')
                {
                    a = 0;
                }
            }

            //Console.Write('\n');

            //Encoding.ASCII.GetString(temp, 0, a);
            string datestring = Encoding.ASCII.GetString(temp, 0, a);
            //Console.WriteLine(datestring);
            long date = Int64.Parse(datestring);
            DateTime date1 = DateTime.FromBinary(date);

            lastlogread = date1;

            int length = 5;
            a = 0;
            while (a < length)
            {
                temp[a] = (byte)logplaybackfile.BaseStream.ReadByte();
                if (a == 1)
                {
                    length = temp[1] + 6 + 2 + 1;
                }
                a++;
            }

            return temp;
        }

        byte[] readlogPacketMavlink()
        {
            byte[] temp = new byte[300];

            MAV.sysid = 0;

            //byte[] datearray = BitConverter.GetBytes((ulong)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds);

            byte[] datearray = new byte[8];

            int tem = logplaybackfile.BaseStream.Read(datearray, 0, datearray.Length);

            Array.Reverse(datearray);

            DateTime date1 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            UInt64 dateint = BitConverter.ToUInt64(datearray, 0);

            try
            {
                // array is reversed above
                if (datearray[7] == 254)
                {
                    //rewind 8bytes
                    logplaybackfile.BaseStream.Seek(-8, SeekOrigin.Current);
                }
                else
                {
                    date1 = date1.AddMilliseconds(dateint / 1000);

                    lastlogread = date1.ToLocalTime();
                }
            }
            catch { }

            MAV.cs.datetime = lastlogread;

            int length = 5;
            int a = 0;
            while (a < length)
            {
                if (logplaybackfile.BaseStream.Position == logplaybackfile.BaseStream.Length)
                    break;
                temp[a] = (byte)logplaybackfile.ReadByte();
                if (temp[0] != 'U' && temp[0] != 254)
                {
                    a = 0;
                    continue;
                }
                if (a == 1)
                {
                    length = temp[1] + 6 + 2; // 6 header + 2 checksum
                }
                a++;
            }

            // set ap type for log file playback
            if (temp[5] == 0 && a > 5)
            {
                mavlink_heartbeat_t hb = temp.ByteArrayToStructure<mavlink_heartbeat_t>(6);
                if (hb.type != (byte)MAVLink.MAV_TYPE.GCS)
                {
                    mavlinkversion = hb.mavlink_version;
                    MAV.aptype = (MAV_TYPE)hb.type;
                    MAV.apname = (MAV_AUTOPILOT)hb.autopilot;
                }
            }

            return temp;
        }

        public bool translateMode(string modein, ref MAVLink.mavlink_set_mode_t mode)
        {
            mode.target_system = MAV.sysid;

            try
            {
                List<KeyValuePair<int, string>> modelist = null;

                foreach (KeyValuePair<int, string> pair in modelist)
                {
                    if (pair.Value.ToLower() == modein.ToLower())
                    {
                        mode.base_mode = (byte)MAVLink.MAV_MODE_FLAG.CUSTOM_MODE_ENABLED;
                        mode.custom_mode = (uint)pair.Key;
                    }
                }

                if (mode.base_mode == 0)
                {
                    MessageBox.Show("No Mode Changed " + modein);
                    return false;
                }
            }
            catch { System.Windows.Forms.MessageBox.Show("Failed to find Mode"); return false; }

            return true;
        }

        public override string ToString()
        {
            return "MAV " + MAV.sysid + " on " + BaseStream.PortName;
        }


        public void Dispose()
        {
            this.Close();
        }
    }
}