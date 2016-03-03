using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using SHDocVw;
using mshtml;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading;
using System.Drawing.Drawing2D;
using Microsoft.Expression.Encoder.ScreenCapture;
using Microsoft.Expression.Encoder.Devices;
using CsharpUSBJoystick;
using Pinoeye.Comms;

namespace Pinoeye
{
    public partial class Main : Form
    {
        TcpClient Client;
        StreamReader Reader;
        StreamWriter Writer;
        NetworkStream stream;
        Thread ReceiveThread;
        bool Connected;
        string[] viewdata = new string[8];

        private USB_Joystick joystick;

        private int servo1;
        private int servo2;
        private int flap;
        private int mode;
        private int button;
        DateTime lastjoystick = DateTime.Now;
        bool serialThread = false;

        Thread joystickthread;
        Thread serialreaderthread;

        public static int threadrun = 0;
        private delegate void AddTextDelegate(string strText);

        public Main()
        {
            InitializeComponent();
        }
        public static MAVLinkInterface comPort = new MAVLinkInterface();
        public static List<MAVLinkInterface> Comports = new List<MAVLinkInterface>();

        public static Main instance = null;

        private ScreenCapEvents sc = new ScreenCapEvents();
        private ScreenCapEvents sc2 = new ScreenCapEvents();

        public class ScreenCapEvents
        {
            public ScreenCaptureJob sj = new ScreenCaptureJob();

            public ScreenCapEvents() { }

            public void SetVideoRecord()
            {
                //sj.OutputScreenCaptureFileName = @"C:\temp\test.wmv"; // 파일 저장 경로..
                sj.ScreenCaptureVideoProfile.Quality = 100;   
                sj.ScreenCaptureVideoProfile.FrameRate = 30.00;
                sj.CaptureMouseCursor = false; // 마우스 표시 여부
                //sj.AddAudioDeviceSource(EncoderDevices.FindDevices(EncoderDeviceType.Audio).ElementAt(2)); // 사운드 장치 설정
                sj.Start();
            }
        }
        private void Form1_Load(object sender, EventArgs e)
        {
            textBox4.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            hud1.Enabled = false;
            hud1.Visible = false;
            this.KeyPreview = true;
            try
            {
                joystick = new USB_Joystick(800, 2200, .99, this); // 조이스틱 범위, 감도
            }
            catch (Exception)
            {
                MessageBox.Show("조이스틱이 연결되지 않았습니다.");
                Application.ExitThread();
                Environment.Exit(0);
            }
        }

        private void disconnect()
        {
            threadrun = 0;
            timer1.Stop();
            button1.Text = "Connect";
            textBox1.ReadOnly = false;
            textBox2.ReadOnly = false;
            textBox3.ReadOnly = false;
            radioButton1.Checked = false;
            radioButton2.Checked = false;
            radioButton1.Text = "서버 : Disconnected";
            radioButton2.Text = "보드 : Disconnected";
            Client.Close();
            Reader.Close();
            Writer.Close();
            stream.Close();
            ReceiveThread.DisableComObjectEagerCleanup();
            textBox5.Clear();
            textBox6.Clear();
            textBox7.Clear();
            textBox8.Clear();
            textBox9.Clear();
            textBox10.Clear();
            textBox11.Clear();
            textBox12.Clear();
            textBox13.Clear();
            textBox14.Clear();
            textBox15.Clear();
            textBox16.Clear();
            textBox17.Clear();
            textBox18.Clear();
            textBox19.Clear();
            textBox20.Clear();
            textBox21.Clear();
            textBox22.Clear();
            textBox23.Clear();
            textBox24.Clear();
            textBox25.Clear();
            Connected = false;
            hud1.Enabled = false;
            hud1.Visible = false;
        }
        private void about_blank()
        {
            webControl1.Source = new System.Uri("about:blank");
            webControl2.Source = new System.Uri("about:blank");
            webControl3.Source = new System.Uri("about:blank");
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (radioButton1.Checked == true)
            {
                serialThread = false;
                comPort.Close();
                timer1.Stop();
                about_blank();
                disconnect();
            }
            else
            {

                if (textBox1.Text == "" || textBox2.Text == "" || textBox3.Text == "")
                {
                    MessageBox.Show("IP 또는 Port가 입력되지 않았습니다.");
                }
                else
                {
                    String IP = textBox1.Text;
                    int port = Convert.ToInt32(textBox2.Text);
                    int ser2port = Convert.ToInt32(textBox3.Text);
                    Connection(IP, port);
                }
            }
        }

        private void Connection(string IP, int port)
        {
            try
            {
                // 카메라
                webControl1.Source = new System.Uri("http://" + IP + ":8080/?action=stream");
                webControl2.Source = new System.Uri("http://" + IP + ":8081/?action=stream");

                // 구글맵
                webControl3.Source = new System.Uri("http://" + IP + ":8080/gmap.html");

                // 서버 접속
                Client = new TcpClient();
                Client.Connect(IP, port);
                stream = Client.GetStream();
                Connected = true;
                Reader = new StreamReader(stream);
                Writer = new StreamWriter(stream);
                ReceiveThread = new Thread(new ThreadStart(Receive));
                ReceiveThread.Start();
                button1.Text = "Disconnent";
                textBox1.ReadOnly = true;
                textBox2.ReadOnly = true;
                textBox3.ReadOnly = true;
                radioButton1.Checked = true;
                radioButton1.Text = "서버 : Connected";

                //조이스틱
                joystick.PresetValues();
                timer1.Start();
               
                // APM 접속
                comPort.BaseStream = new TcpSerial();
                comPort.BaseStream.Host = IP;
                comPort.BaseStream.Port = textBox3.Text;
                comPort.MAV.cs.ResetInternals();
                comPort.BaseStream.DtrEnable = false;
                comPort.BaseStream.RtsEnable = false;
                comPort.giveComport = false;
                comPort.Open(true);
                if (comPort.BaseStream.IsOpen)
                {
                    radioButton2.Checked = true;
                    radioButton2.Text = "보드 : Connected";
                    System.Threading.ThreadPool.QueueUserWorkItem(mainloop);
                    hud1.Enabled = true;
                    hud1.Visible = true;
                }
                else 
                {
                    about_blank();
                    disconnect();
                    throw new Exception("APM 접속 실패"); 
                }
                mode = 1360; // 수동 조종 (1360: 수동조종, 1361: 자세제어)
                flap = 1400; // OFF (1100: ON, 1400:OFF)
                servo1 = 1700; // TRIM
                servo2 = 1500; // TRIM
                joystickthread = new Thread(new ThreadStart(joysticksend))
                {
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal,
                    Name = "Main joystick sender"
                };
                joystickthread.Start();

                // 시리얼 리더
                serialreaderthread = new Thread(SerialReader)
                {
                    IsBackground = true,
                    Name = "Main Serial reader",
                    Priority = ThreadPriority.AboveNormal
                };
                serialreaderthread.Start();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
            }
        }
        private delegate void myDelegate();
        private void Receive()
        {
            try
            {
                while (Connected)
                {
                    Thread.Sleep(1);
                    Writer.WriteLine("\0"); // heartbeat
                    Writer.Flush();
                    string tempStr=Reader.ReadLine();
                    if ((tempStr.Substring(0,1)) == "@")
                    {                        
                        string[] receivedata = tempStr.Split('@');                        
                        
                        if (receivedata != null)
                        {
                            
                            for (int i = 0; i < 8; i++)
                            {
                                viewdata[i] = receivedata[i];
                                this.Invoke(new myDelegate(dataview));
                            }
                            Array.Clear(receivedata, 0, receivedata.Length);
                            tempStr = "\0";
                        }
                      
                    }
                } 
            }
            catch
            {
                serialThread = false;
                comPort.Close();
                timer1.Stop();
                this.Invoke(new myDelegate(about_blank));
                this.Invoke(new myDelegate(disconnect));
                MessageBox.Show("연결이 중단 되었습니다.");
            }
            
        }
        private void dataview()
        {
            textBox5.Text = viewdata[1] + " Mb/s"; // 비트레이트
            textBox6.Text = viewdata[2]; // 링크 품질
            textBox7.Text = viewdata[3]; // 신호 수준
            textBox8.Text = viewdata[4]; // 잡음 레벨
            textBox9.Text = viewdata[5] + " ℃"; // 온도
           
            if (viewdata[6] != null)
            {
                if ((viewdata[6].Substring(0, 2)).Equals("10")) textBox10.Text = "1000 Mhz";
                if ((viewdata[6].Substring(0, 3)).Equals("115")) textBox10.Text = "1150 Mhz";
                else textBox10.Text = viewdata[6].Substring(0,3) + " Mhz"; // 클럭
            }

            textBox11.Text = comPort.MAV.cs.battery_voltage.ToString() + " V"; // 배터리 전압
            textBox13.Text = comPort.MAV.cs.battery_remaining.ToString() + " %"; // 배터리 잔량

            textBox14.Text = comPort.MAV.cs.roll.ToString("N2"); // 롤
            textBox15.Text = comPort.MAV.cs.pitch.ToString("N2"); // 피치
            textBox16.Text = comPort.MAV.cs.yaw.ToString("N2"); // 요
            
            textBox19.Text = comPort.MAV.cs.ch1out.ToString(); // ch1
            textBox20.Text = comPort.MAV.cs.ch2out.ToString(); // ch2
            textBox21.Text = comPort.MAV.cs.ch4out.ToString(); // ch3
            textBox22.Text = comPort.MAV.cs.ch3out.ToString(); // ch4

            if (viewdata[7] != null)
            {
                        try
                        {                            
                            string[] gps = viewdata[7].Split(',');
                            float spd = System.Convert.ToSingle(gps[2]);
                            float alt = System.Convert.ToSingle(gps[3]);

                            textBox17.Text = string.Format("{0:0.0}", spd); // 속도
                            textBox23.Text = gps[0].Trim(); // 위도
                            textBox24.Text = gps[1].Trim(); // 경도
                            textBox25.Text = string.Format("{0:0.0}", alt); // 고도

                            textBox17.AppendText(" Km/h");
                            textBox25.AppendText(" m");
                        }
                        catch {
                            textBox17.Text = "ERROR";
                            textBox23.Text = "ERROR";
                            textBox24.Text = "ERROR";
                            textBox25.Text = "ERROR";
                        }
            }
            else
            {
                textBox17.Text = "ERROR";
                textBox23.Text = "ERROR";
                textBox24.Text = "ERROR";
                textBox25.Text = "ERROR";
            }
        }
        
        public Rectangle GetPanelLocation(bool left)
        {
            Rectangle rc;

            if (left) rc = new Rectangle(webControl1.PointToScreen(webControl1.ClientRectangle.Location), webControl1.Size);
            else rc = new Rectangle(webControl2.PointToScreen(webControl2.ClientRectangle.Location), webControl2.Size);

            rc.Width = (rc.Width % 2 == 0) ? rc.Width : rc.Width + 1;
            rc.Height = (rc.Height % 2 == 0) ? rc.Height : rc.Height + 1;

            return rc;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            string time = System.DateTime.Now.ToString("yyyy년MM월dd일hh시mm분ss초");
            Bitmap bitmap = new Bitmap(this.webControl2.Width, this.webControl2.Height);
            Graphics g = Graphics.FromImage(bitmap);
            g.CopyFromScreen(
                PointToScreen(new Point(this.webControl2.Location.X, this.webControl2.Location.Y)),
                new Point(0, 0),
                this.webControl2.Size);
            bitmap.Save(textBox4.Text + "\\cam2_" + time + ".jpg");
        }

        private void button9_Click(object sender, EventArgs e)
        {
            string time = System.DateTime.Now.ToString("yyyy년MM월dd일hh시mm분ss초");
            var sj = sc.sj;
            
            sj.CaptureRectangle = GetPanelLocation(true);
            sj.OutputScreenCaptureFileName = textBox4.Text + "\\cam1_" + time + ".wmv";
            sc.SetVideoRecord();

            Thread.Sleep(10);

            string time2 = System.DateTime.Now.ToString("yyyy년MM월dd일hh시mm분ss초");
            var sj2 = sc2.sj;

            sj2.CaptureRectangle = GetPanelLocation(false);
            sj2.OutputScreenCaptureFileName = textBox4.Text + "\\cam2_" + time2 + ".wmv";
            sc2.SetVideoRecord();
            
            button3.Enabled = false;
            button4.Enabled = false;
            button7.Enabled = false;
            button8.Enabled = false;
            button9.Enabled = false;
            button10.Enabled = true;

            this.Activate();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            string time = System.DateTime.Now.ToString("yyyy년MM월dd일hh시mm분ss초");
            var sj = sc.sj;
            sj.CaptureRectangle = GetPanelLocation(true);
            sj.OutputScreenCaptureFileName = textBox4.Text + "\\cam1_" + time + ".wmv";
            sc.SetVideoRecord();
            button3.Enabled = false;
            button4.Enabled = true;

            this.Activate();
        }

        private void button6_Click(object sender, EventArgs e)
        {
            string temp = textBox4.Text;
            string time = System.DateTime.Now.ToString("yyyy년MM월dd일hh시mm분ss초");
            Bitmap bitmap = new Bitmap(this.webControl1.Width, this.webControl1.Height);
            Graphics g = Graphics.FromImage(bitmap);
            g.CopyFromScreen(
                PointToScreen(new Point(this.webControl1.Location.X, this.webControl1.Location.Y)),
                new Point(0, 0),
                this.webControl1.Size);
            bitmap.Save(textBox4.Text + "\\cam1_" + time + ".jpg");
        }

        private void button8_Click(object sender, EventArgs e)
        {
            string time = System.DateTime.Now.ToString("yyyy년MM월dd일hh시mm분ss초");
            var sj = sc2.sj;
            sj.CaptureRectangle = GetPanelLocation(false);
            sj.OutputScreenCaptureFileName = textBox4.Text + "\\cam2_" + time + ".wmv";
            sc2.SetVideoRecord();
            button8.Enabled = false;
            button7.Enabled = true;

            this.Activate();
        }

        private void button4_Click(object sender, EventArgs e)
        {
            sc.sj.Stop();
            button3.Enabled = true;
            button4.Enabled = false;
        }

        private void button7_Click(object sender, EventArgs e)
        {
            sc2.sj.Stop();
            button8.Enabled = true;
            button7.Enabled = false;
        }

        private void button10_Click(object sender, EventArgs e)
        {
            sc.sj.Stop();
            sc2.sj.Stop();
            button3.Enabled = true;
            button4.Enabled = false;
            button8.Enabled = true;
            button7.Enabled = false;
            button9.Enabled = true;
            button10.Enabled = false;
        }

        private void button5_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1.ShowDialog();
            string path = folderBrowserDialog1.SelectedPath;
            textBox4.Text = path;
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            Application.ExitThread();
            Environment.Exit(0);
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            joystick.GetData();
            UpdateFormControls();
        }

        private void UpdateFormControls()
        {
            //joystick_text = null;

            int[] pov = joystick.State.GetPointOfView();     

            for (int i = 0; i < 7; i++) 
                if (joystick.ButtonPressed(i) == true) button = i;
             
            if (pov[0] == 0) { // up
                if (servo2 < 1800) servo2 += 10;
            }
            if (pov[0] == 4500) // up right
            {
                if (servo2 < 1800) servo2 += 10;
                if (servo1 < 2000) servo1 += 10;
            }
            if (pov[0] == 9000) // right
            {
                if (servo1 < 2000) servo1 += 10;
            }
            if (pov[0] == 13500) // down right
            {
                if (servo2 > 1000) servo2 -= 10;
                if (servo1 < 2000) servo1 += 10;
            }
            if (pov[0] == 18000) // down
            {
                if (servo2 > 1400) servo2 -= 10;
            }
            if (pov[0] == 22500) // down left
            {
                if (servo2 > 1000) servo2 -= 10;
                if (servo1 > 1400) servo1 -= 10;
            }
            if (pov[0] == 27000) // left
            {
                if (servo1 > 1400) servo1 -= 10;
            }
            if (pov[0] == 31500) // up left
            {
                if (servo2 < 1800) servo2 += 10;
                if (servo1 > 1400) servo1 -= 10;
            }

            if (button == 1) // 2번 카메라 캡쳐
            {
                string time = System.DateTime.Now.ToString("yyyy년MM월dd일hh시mm분ss초");
                Bitmap bitmap = new Bitmap(this.webControl2.Width, this.webControl2.Height);
                Graphics g = Graphics.FromImage(bitmap);
                g.CopyFromScreen(
                    PointToScreen(new Point(this.webControl2.Location.X, this.webControl2.Location.Y)),
                    new Point(0, 0),
                    this.webControl2.Size);
                bitmap.Save(textBox4.Text + "\\cam2_" + time + ".jpg");
            }

            if (button == 2) // 2번 카메라 중립
            {
                servo1 = 1700;
                servo2 = 1500;
            }

            button = 0; // 버튼 고정 효과 제거

            // 플랩
            if (flap == 1400) textBox12.Text = "OFF";
            else textBox12.Text = "ON";
            // 모드
            if (mode == 1361) textBox18.Text = "자세 제어";
            else textBox18.Text = "수동 조종";
        }

        private int joystick_reverse(int x, int y, int z)
        {
            // x는 최소값
            // y는 최대값
            // z는 입력값
            int mid = (x + y) / 2;
            return mid * 2 - z;
        }

     private void joysticksend()
     {
            float rate = 50;
            DateTime lastratechange = DateTime.Now;

            while (true)
            {
                MAVLink.mavlink_rc_channels_override_t rc = new MAVLink.mavlink_rc_channels_override_t();

                rc.target_component = comPort.MAV.compid;
                rc.target_system = comPort.MAV.sysid;

                    rc.chan1_raw = (ushort)joystick_reverse(800, 2200, joystick.State.X); // 에일러론
                    rc.chan2_raw = (ushort)joystick_reverse(800, 2200, joystick.State.Y); // 엘리베이터
                    rc.chan3_raw = (ushort)joystick_reverse(800, 2200, joystick.State.Z); // 스로틀
                    rc.chan4_raw = (ushort)joystick_reverse(800, 2200, joystick.State.Rz); // 러더 + 랜딩기어

                    rc.chan5_raw = (ushort)flap; // 플랩
                    rc.chan6_raw = (ushort)servo1; // 카메라 1
                    rc.chan7_raw = (ushort)servo2; // 카메라 2
                    rc.chan8_raw = (ushort)mode; // 모드

                    // 에일러론 한계치 조정
                    if (rc.chan1_raw > 1800) rc.chan1_raw = (ushort)1800;
                    else if (rc.chan1_raw < 1200) rc.chan1_raw = 1200;

                if (lastjoystick.AddMilliseconds(rate) < DateTime.Now)
                {
                    comPort.sendPacket(rc);
                    lastjoystick = DateTime.Now;
                }
                Thread.Sleep(20);
            }
        }

        
        private void textBox1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && radioButton1.Checked == false)
            {
                this.button1_Click(sender, e);
            }

        }

        private void textBox2_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && radioButton1.Checked == false)
            {
                this.button1_Click(sender, e);
            }
        }
        private void mainloop(object o)
        {
            System.Threading.Thread.CurrentThread.IsBackground = true;

            threadrun = 1;

            DateTime lastdata = DateTime.MinValue;

            while (threadrun == 1)
            {
                if (comPort.giveComport == true)
                {
                    System.Threading.Thread.Sleep(50);
                    continue;
                }
                if (!comPort.BaseStream.IsOpen) lastdata = DateTime.Now;
                if (!(lastdata.AddSeconds(8) > DateTime.Now) && comPort.BaseStream.IsOpen)
                {
                    comPort.requestDatastream(MAVLink.MAV_DATA_STREAM.RAW_SENSORS, comPort.MAV.cs.ratesensors);
                    comPort.requestDatastream(MAVLink.MAV_DATA_STREAM.RC_CHANNELS, comPort.MAV.cs.raterc);
                }
                lastdata = DateTime.Now.AddSeconds(60);
                Main.comPort.readPacket();
                updateBindingSource();
            }
        }

        DateTime lastscreenupdate = DateTime.Now;
        private void updateBindingSource()
        {
            if (lastscreenupdate.AddMilliseconds(40) < DateTime.Now)
            {
                this.BeginInvoke((System.Windows.Forms.MethodInvoker)delegate()
                {
                    try
                    {
                        Main.comPort.MAV.cs.UpdateCurrentSettings(bindingSourceHud);
                        lastscreenupdate = DateTime.Now;
                    }
                    catch { }
                });
            }
        }
        private void SerialReader()
        {
            if (serialThread == true)
                return;
            serialThread = true;

            int minbytes = 0;

            while (serialThread)
            {
                try
                {
                    Thread.Sleep(1);
                    if (!comPort.BaseStream.IsOpen || comPort.giveComport == true)
                    {
                        if (!comPort.BaseStream.IsOpen)
                        {
                            foreach (var port in Comports)
                            {
                                if (port.BaseStream.IsOpen)
                                {
                                    comPort = port;
                                    break;
                                }
                            }
                        }

                        System.Threading.Thread.Sleep(100);
                        continue;
                    }

                    while (comPort.BaseStream.IsOpen && comPort.BaseStream.BytesToRead > minbytes && comPort.giveComport == false)
                    {
                        try
                        {
                            comPort.readPacket();
                        }
                        catch { }
                    }

                    try
                    {
                        comPort.MAV.cs.UpdateCurrentSettings(null, false, comPort);
                    }
                    catch { }

                    foreach (var port in Comports)
                    {
                        if (!port.BaseStream.IsOpen)
                        {
                            Comports.Remove(port);
                            break;
                        }
                        if (port == comPort)
                            continue;
                        while (port.BaseStream.IsOpen && port.BaseStream.BytesToRead > minbytes)
                        {
                            try
                            {
                                port.readPacket();
                            }
                            catch { }
                        }
                        try
                        {
                            port.MAV.cs.UpdateCurrentSettings(null, false, port);
                        }
                        catch { }
                    }
                }
                catch (Exception)
                {
                    try
                    {
                        comPort.Close();
                    }
                    catch { }
                }
            }
            Console.WriteLine("SerialReader Done");
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            switch(e.KeyCode) {
                case Keys.Q: // 1번 카메라 캡쳐
                    this.button6_Click(sender, e);
                    break;
                case Keys.W: // 2번 카메라 캡쳐
                    this.button2_Click(sender, e);
                    break;
                case Keys.A: // 1번 카메라 녹화 / 중지
                    if(button3.Enabled==true)
                        this.button3_Click(sender, e);
                    else
                        this.button4_Click(sender, e);
                    break;
                case Keys.S: // 2번 카메라 녹화 / 중지
                     if(button8.Enabled==true)
                        this.button8_Click(sender, e);
                     else
                        this.button7_Click(sender, e);
                    break;
                case Keys.Z: // 동시 녹화
                    if(button9.Enabled==true)
                        this.button9_Click(sender, e);
                    else
                        this.button10_Click(sender, e);
                    break;
                case Keys.F: // 플랩 온/오프
                    if (flap == 1100) flap = 1400;
                    else flap = 1100;
                    break;
                case Keys.M: // 비행 모드 전환
                    if(mode == 1360) mode = 1361;
                    else mode = 1360;
                    break;
                // 카메라
                case Keys.Left:
                    if (servo1 > 1400) servo1 -= 10;
                    break;
                case Keys.Right:
                    if (servo1 < 2000) servo1 += 10;
                    break;
                case Keys.Up:
                    if (servo2 < 1800) servo2 += 10;
                    break;
                case Keys.Down:
                    if (servo2 > 1000) servo2 -= 10;
                    break;
            }
        }

        private void Main_FormClosing(object sender, FormClosingEventArgs e)
        {
            serialThread = false;
            threadrun = 0;
            comPort.Close();
            timer1.Stop();
            joystick.Dispose();
            this.Dispose();
        }
    }
}