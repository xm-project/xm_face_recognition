/*
 * DEBUG,显示运行时间等信息；
 * USING_FILE，将程序识别到的人脸存储在文件中；
 */
//#define DEBUG
//#define USING_FILE
#define FACE_DEBUG

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Luxand;
using System.Threading;
using System.IO;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;

/*
 * facerecongnize program 
 * 返回模式 
 * 接收     0x01+namelen+name=ProgramState.AddFace
 *          0x02=ProgramState.psRecognize
 *          0x03=stop
 *          
 * 返回     0x00=fail
 *          0x01=success
 *          0x02+namelen+name=success
 *
 
 * 
无人01；熟人02;生人03；多人同时存在04
 *
 * 测试数据
记住 10张 1089.5719毫秒  
          1090.3136
          1057.3944
     20张 2106.7524
          2112.1317

无人状态 100张 4844.7476毫秒
               4797.1812
         50张  2463.0872毫秒
	       2414.7188

识别 10张 1102.3359毫秒
          1048.3943    
     20张 2108.0998
          2132.6496
 * 且在模板有100张的情况下识别速率影响不大（几乎无影响）

无人状态 100张 4757.4898毫秒
               4830.9478
         50张  2446.0872毫秒
	       2526.0334
 * 
 * */

namespace Face
{
    public partial class MainFrame : Form
    {
#if TIME_DEBUG
        System.Diagnostics.Stopwatch stopwatch = new Stopwatch();
#endif
        //控制变量
        bool AddNewOne = false;
        int btn1Count = -1;
        int btn2Count = -1;
        bool btn1Click = false;
        bool btn2Click = false;
        string btn1UserName = "";
        string btn2UserName = "";
        //状态变量
        enum ProgramState { psNormal, psRecognize, psAddFace, psNothing ,psFaceOnly};
        ProgramState programState = ProgramState.psNormal;
        //图形
        Graphics gr;
        //人脸模板
        struct FaceTemplate
        { // single template
            public byte[] templateData;
        }
        List<FaceTemplate> faceTemplates; // set of face templates (we store 10)
        List<List<FaceTemplate>> UserTemplates;
        //摄像头
        int CameraHandle = 0;
        String cameraName;
        bool NeedClose = false;
        FSDKCam.VideoFormatInfo[] FormatList;
        int choosenFormat = 0;
        //人脸信息
        List<string> userName;
        // WinAPI procedure to release HBITMAP handles returned by FSDKCam.GrabFrame
        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr hObject);
        string username = "";
        //网络相关
        const int port = 10002;
        //const int remotePort = 10000;
        int[] who=new int[REPEAT_REC];
        //const string host = "10.12.1.33";
        int recperson=-1;
        TcpListener myListener;

        bool ok = false;
        //用作互斥
        Object obj = new Object();
        //常量
        const int REPEAT_REC=10;
        const int REPEAT_REM=10;
        const int MAX_MINBEAR = 20;
        const int MAX_MAXBEAR = 20;


        public MainFrame()
        {
            InitializeComponent();
        }

        //网络部分
        private void startServer()
        {
            try
            {
                myListener = new TcpListener(port);
                Console.WriteLine("Server Start");
                myListener.Start();

                while (true)
                {
                    TcpClient client = myListener.AcceptTcpClient();
                    lock (obj)
                    {
                        ok = false;
                    }
                    NetworkStream ns = client.GetStream();
                    byte[] lenByte = this.recvLen(ns, 4);
                    int len = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lenByte, 0));
                    byte[] data = this.recvLen(ns, len);

                    switch (data[0])
                    {
                        //rem
                        case 0x01:
                            Console.WriteLine("rember command");
#if FACE_DEBUG
                        Console.WriteLine("rember command");
#endif
                            int nameLen = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, 1));
                            String name = Encoding.Default.GetString(data, 5, nameLen);
#if FACE_DEBUG
                        Console.WriteLine(name);
#endif
                            //handle
                            lock (obj)
                            {
                                recperson = -2;
                                username = name;
                                AddNewOne = true;
                                programState = ProgramState.psAddFace;
                                while (!ok)
                                    Monitor.Wait(obj);
                            }
                            switch (recperson)
                            {
                                case -1:
                                    {
                                        this.sendData(ns, new byte[] { 0x00 });
                                        break;
                                    }
                                case -2:
                                    {
                                        this.sendData(ns, new byte[] { 0x01 });
                                        break;
                                    }
                                case -3:
                                    {
                                        this.sendData(ns, new byte[] { 0x03 });
                                        break;
                                    }

                            }
#if FACE_DEBUG
                        Console.WriteLine("rember response OK!");
#endif
                            break;
                        case 0x02:
                            //start recognize
                            Console.WriteLine("recongnize command");

                            lock (obj)
                            {
                                recperson = -1;
                                programState = ProgramState.psRecognize;
                                while (!ok)
                                    Monitor.Wait(obj);
                            }

                            if (recperson > -1)
                            {
#if FACE_DEBUG
                            Console.WriteLine("recongnize response:User    " + userName[recperson]);
#endif
                                List<byte> list = new List<byte>();
                                list.Add(0x02);
                                list.AddRange(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(userName[recperson].Length)));
                                list.AddRange(Encoding.Default.GetBytes(userName[recperson]));
                                this.sendData(ns, list.ToArray());
                            }
                            else if (recperson == -2)
                            {
#if FACE_DEBUG
                            Console.WriteLine("recongnize response:No user");
#endif
                                this.sendData(ns, new byte[] { 0x01 });
                            }
                            else if (recperson == -1)
                            {
#if FACE_DEBUG
                            Console.WriteLine("recongnize response:Stranger");
#endif
                                this.sendData(ns, new byte[] { 0x03 });
                            }
                            else if (recperson == -3)
                            {
#if FACE_DEBUG
                            Console.WriteLine("recongnize response:Multi person");
#endif
                                this.sendData(ns, new byte[] { 0x04 });
                            }
                            break;
                    }
                    ns.Close();
                    client.Close();
                }
                /*
                while (true)
                {
                    bytes = c.Receive(recevBytes, recevBytes.Length, 0);
                   // label1.Text = bytes.ToString();
                    if (bytes > 0)
                    {
                        sendmsg = false;
                        analyzing(recevBytes);
                        while (!sendmsg) { }//waiting for ok
                    }
                    else continue;
                }*/
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
        private byte[] recvLen(Stream stream, int len)
        {
            byte[] data = new byte[len];
            int offset = 0;
            while (len > 0)
            {
                int n = stream.Read(data, offset, len);
                len -= n;
                offset += n;
            }
            return data;
        }
        private void sendData(Stream stream,byte[] data)
        {
            List<byte> list = new List<byte>();
            byte[] lenBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(data.Length));
            list.AddRange(lenBytes);
            list.AddRange(data);
            stream.Write(list.ToArray(), 0, list.Count);
        }

        //投票处理部分
        int vote()
        {
            int[] voteman = new int[userName.Count+1];
            int i = 0;
            for (; i < who.Length; i++)
                voteman[who[i]+1]++;
            int maxnum = 0;
            for (i = 0; i < voteman.Length; i++)
                if (maxnum < voteman[i])
                    maxnum = i;
            return maxnum-1;

        }
        void reset()
        {
            int i = 0;
            for (; i < who.Length; i++)
                who[i] = -1;
        }

        //载入部分
        private void Form1_Load(object sender, EventArgs e)
        {
#if TIME_DEBUG
            Control.CheckForIllegalCrossThreadCalls = false;
#endif
            //激活SDK
            if (FSDK.FSDKE_OK != FSDK.ActivateLibrary("PA+dByqrisjysrbGua7Vt8L1jSHlb4o1NEAQ2rLWbLjwTffKmWgmaTYtyua4lLceskJxZCFPYIBAEcyyn7U6A/7uHTvjTWVsc5KBvdZSOt8iL3D2wVLGrWQaRgD7NKxFIBRZu2YuEKFrivcdwjm1oM8UE72dHKdxoHSvUoTf7l0="))
            {
                MessageBox.Show("Please run the License Key Wizard (Start - Luxand - FaceSDK - License Key Wizard)", "Error activating FaceSDK", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            }
            FSDK.InitializeLibrary();
            FSDKCam.InitializeCapturing();
            //读取摄像头信息(默认载入第一个摄像头的第一种格式)
            string[] CameraList;
            int Count;
            FSDKCam.GetCameraList(out CameraList, out Count);
            if (0 == Count)
            {
                Console.WriteLine("No CameraConnected ");
                MessageBox.Show("Camera wrong", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            }
            /*
            for (int i = 0; i < CameraList.Length ; i++)
            {
                Console.WriteLine("Camera "+i+" : "+CameraList[i]);
            }
            */
            //Console.WriteLine("Choose the camera you want to use");
            //int choosenCamera = Convert.ToInt32( Console.ReadLine());
            int choosenCamera = 0;
            FSDKCam.GetVideoFormatList(ref CameraList[choosenCamera], out FormatList, out Count);
            /*
            for (int i = 0; i < FormatList.Length; i++)
            {
                Console.WriteLine("Format " + i + " : " + FormatList[i].Width.ToString()+" X "+FormatList[i].Height.ToString());
            }
            */
            Console.WriteLine("Choose the format you want to use");
            //choosenFormat = Convert.ToInt32(Console.ReadLine());
            choosenFormat = 0;
            cameraName = CameraList[choosenCamera];
            //调整窗口大小
            //resize(FormatList[choosenFormat].Width,FormatList[choosenFormat].Height);
            //变量空间申明
            faceTemplates = new List<FaceTemplate>();
            UserTemplates = new List<List<FaceTemplate>>();
            userName = new List<string>();
            username = "";
            //文件载入
            //loadfile();
            //网络初始化
            reset();
            Thread netserver = new Thread(new ThreadStart(this.startServer));
            netserver.Start();
            //人脸程序初始化
            Thread faceserver = new Thread(new ThreadStart(this.start));
            faceserver.Start();
        }  
        void resize(int w, int h)
        {
            pictureBox1.Width = w;
            pictureBox1.Height = h;
            this.Width = pictureBox1.Width;
            this.Height = pictureBox1.Height + 50;

            int a = pictureBox1.Width;
            int b = pictureBox1.Height;
            int c = this.Width; ;
            int d = this.Height;
        }
        private int CameraStart()
        {
            //摄像头启动
            int r = FSDKCam.OpenVideoCamera(ref cameraName, ref CameraHandle);
            if (r != FSDK.FSDKE_OK)
            {
                MessageBox.Show("Error opening the first camera", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            }
            FSDKCam.SetVideoFormat(ref cameraName, FormatList[choosenFormat]);
            return CameraHandle;
        }

        //主处理部分
        private void start()
        {
            loadUsers();
                NeedClose = false;
                int CameraHandle = CameraStart();
                //设置监测条件  其中
                /*
                 * SetFaceDetectionParameters(false, false, 100);参数1将人脸的检测角度从正负15°拓展到30°
                 *                                               参数2将决定是否检测人脸所处的角度
                 *                                               参数3是缩放大小 值越大质量越高
                 *现在有效距离1米  最大有效距离1.3米
                /*
                 * SetFaceDetectionThreshold(3);设置对于人脸的敏感性 数值越高就越对于人脸不敏感 只能在人脸十分清晰的时候才能将其检测
                 */
                FSDK.SetFaceDetectionParameters(true, false, 100);
                FSDK.SetFaceDetectionThreshold(1);
                while (!NeedClose)
                {
                    try
                    {
                        switch (programState)
                        {
                            case ProgramState.psNormal:
                                Normalhandle();
                                break;
                            case ProgramState.psAddFace:
                                AddFacehandle();
                                lock (obj)
                                {
                                    ok = true;
                                    Monitor.PulseAll(obj);
                                    programState = ProgramState.psNormal;
                                }
                                break;
                            case ProgramState.psRecognize:
                                Recongnizehandle();
                                lock (obj)
                                {
                                    ok = true;
                                    Monitor.PulseAll(obj);
                                    programState = ProgramState.psNormal;
                                }
                                break;
                            case ProgramState.psNothing:
                                Nothinghandle();
                                break;
                        }
                        Application.DoEvents();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }
                }
        }

        int Normalhandle()
        {
            try
            {
                Int32 ImageHandle = 0;
                if (FSDK.FSDKE_OK != FSDKCam.GrabFrame(CameraHandle, ref ImageHandle))
                {
                    return -1;
                }
                FSDK.CImage Image = new FSDK.CImage(ImageHandle);
                Image FrameImage = Image.ToCLRImage();
                gr = Graphics.FromImage(FrameImage);
                pictureBox1.Image = FrameImage;

                FSDK.TFacePosition[] FacePosition = Image.DetectMultipleFaces();

                for (int person = 0; person < FacePosition.Length; person++)
                    draw(FacePosition[person]);

                GC.Collect(); // collect the garbage after the deletion
                return 0;
            }
            catch 
            {
                return -1;
            }
        }
        int AddFacehandle()
        {

#if TIME_DEBUG
            label2.Text = "Stopwatch REC start";
            stopwatch.Reset();
            stopwatch.Start();
#endif
            faceTemplates.Clear();
            int maxbear = 0;
            int minbear = 0;
            //label1.Text = "Adding new one";
            for (int count = 0; count < REPEAT_REM; count++)
            {
                Int32 ImageHandle = 0;
                if (FSDK.FSDKE_OK != FSDKCam.GrabFrame(CameraHandle, ref ImageHandle))
                {
                    return -1;
                }
                FSDK.CImage Image = new FSDK.CImage(ImageHandle);
                Image FrameImage = Image.ToCLRImage();
                gr = Graphics.FromImage(FrameImage);

                FSDK.TFacePosition[] FacePosition = Image.DetectMultipleFaces();
                //只允许有一个人在检测范围内
                if (FacePosition.Length == 0) 
                {
                    minbear++;
                    //无人脸状态超出忍受值
                    if (minbear > MAX_MINBEAR)
                    {
#if TIME_DEBUG
                        stopwatch.Stop();
                        TimeSpan timespan2 = stopwatch.Elapsed;
                        double milliseconds2 = timespan2.TotalMilliseconds;  //  总毫秒数
                        label2.Text = milliseconds2.ToString() + " .REM";
#endif
                        faceTemplates.Clear();
                        username = "";
                        //label1.Text = "no people";
                        recperson = -2;
                        return -1;
                    }
                    count--;
                    continue;
                }
                if (FacePosition.Length != 1) 
                {
                    maxbear++;
                    //多人脸状态超出忍受值
                    if (maxbear > MAX_MAXBEAR)
                    {
                        faceTemplates.Clear();
                        username = "";
                        //label1.Text = "Too many people";
                        recperson = -3;
                        return -1;
                    }
                    count--;
                    continue;
                }


                //draw(FacePosition[0]);
                FaceTemplate Template = new FaceTemplate();
                FSDK.TPoint[] features = Image.DetectEyesInRegion(ref FacePosition[0]);
                Template.templateData = Image.GetFaceTemplateUsingEyes(ref features);

                faceTemplates.Add(Template);
                Application.DoEvents();
            }
            if (AddNewOne)
            {
                if (btn1Click)
                {
                    btn1Click = false;
                    int i = 0;
                    for (i = 0; i < REPEAT_REM; i++)
                    {
                        int fnum = btn1Count * REPEAT_REM + i;
                        string tpath = ".\\Users\\" + btn1UserName + fnum.ToString() + ".dat";
                        MemoryStream m = new MemoryStream(faceTemplates[i].templateData);
                        FileStream fs = new FileStream(tpath, FileMode.OpenOrCreate);
                        m.WriteTo(fs);
                        m.Close();
                        fs.Close();
                    }
                }
                if (btn2Click)
                {
                    btn2Click = false;
                    int i = 0;
                    for (i = 0; i < REPEAT_REM; i++)
                    {
                        int fnum = btn2Count * REPEAT_REM + i;
                        string tpath = ".\\Users\\" + btn2UserName + fnum.ToString() + ".dat";
                        MemoryStream m = new MemoryStream(faceTemplates[i].templateData);
                        FileStream fs = new FileStream(tpath, FileMode.OpenOrCreate);
                        m.WriteTo(fs);
                        m.Close();
                        fs.Close();
                    }
                }
                if(true)
                {
                    AddNewOne = false;
                    string name = username;
                    username = "";
                    if (namexist(name) == -1)
                    {
                        userName.Add(name);
                        List<FaceTemplate> temp = new List<FaceTemplate>(faceTemplates.ToArray());
                        UserTemplates.Add(temp);
                    }
                    else
                    {
                        List<FaceTemplate> temp = new List<FaceTemplate>(faceTemplates.ToArray());
                        int existnum = namexist(name);
                        int i;
                        for (i = 0; i < temp.Count; i++)
                        {
                            UserTemplates[existnum].Add(temp[i]);
                        }
                    }
                }
            }
            faceTemplates.Clear();
            username = "";
            //label1.Text = "Saved the man";

#if TIME_DEBUG
            stopwatch.Stop();
            TimeSpan timespan = stopwatch.Elapsed; 
            double milliseconds = timespan.TotalMilliseconds;  //  总毫秒数
            label2.Text = milliseconds.ToString()+" .REM";
#endif
            recperson = -1;
            return 0;
        }
        int Recongnizehandle()
        {

#if TIME_DEBUG
            label2.Text = "Stopwatch REC start";
            stopwatch.Reset();
            stopwatch.Start();
#endif
            faceTemplates.Clear();
            //label1.Text = "Recongnize start";
            int maxbear = 0;
            int minbear = 0;
            reset();
            recperson = -1;
            for (int count = 0; count < REPEAT_REC; count++)
            {
                Int32 ImageHandle = 0;
                if (FSDK.FSDKE_OK != FSDKCam.GrabFrame(CameraHandle, ref ImageHandle))
                {
                    return -1;
                }
                FSDK.CImage Image = new FSDK.CImage(ImageHandle);
                Image FrameImage = Image.ToCLRImage();
                gr = Graphics.FromImage(FrameImage);

                FSDK.TFacePosition[] FacePosition = Image.DetectMultipleFaces();
                if (FacePosition.Length == 0)
                {
                    minbear++;
                    //无人脸状态超出忍受值
                    if (minbear > MAX_MINBEAR)
                    {
                        recperson = -2;
#if TIME_DEBUG
                        stopwatch.Stop();
                        TimeSpan timespan2 = stopwatch.Elapsed;
                        double milliseconds2 = timespan2.TotalMilliseconds;  //  总毫秒数
                        label2.Text = milliseconds2.ToString() + " .REM";
#endif
                        return 1;
                    }
                    count--;
                    continue;
                }
                if (FacePosition.Length != 1)
                {
                    maxbear++;
                    //多人脸状态超出忍受值
                    if (maxbear > MAX_MAXBEAR)
                    {
                        recperson = -3;
                        return 1;
                    }
                    count--;
                    continue;
                }

                //draw(FacePosition[0]);

                FaceTemplate Template = new FaceTemplate();
                FSDK.TPoint[] features = Image.DetectEyesInRegion(ref FacePosition[0]);

                Template.templateData = Image.GetFaceTemplateUsingEyes(ref features);
                
                int recnum = recongnize(Template);
                faceTemplates.Add(Template);

                who[count] = recnum;

                if (recnum != -1)
                {
                    StringFormat format = new StringFormat();
                    format.Alignment = StringAlignment.Center;
                    gr.DrawString(userName[recnum], new System.Drawing.Font("Arial", 16),
                                                        new System.Drawing.SolidBrush(System.Drawing.Color.LightGreen),
                                                        FacePosition[0].xc, FacePosition[0].yc + FacePosition[0].w * 0.55f, format);
                }
                Application.DoEvents();
            }

            recperson = vote();
            //label1.Text = "Recongnize the man";

            if (recperson > -1)
            {
                List<FaceTemplate> temp = new List<FaceTemplate>(faceTemplates.ToArray());
                int i;
                for (i = 0; i < temp.Count; i++)
                {
                    UserTemplates[recperson].Add(temp[i]);
                }
            }

#if TIME_DEBUG
            stopwatch.Stop();
            TimeSpan timespan = stopwatch.Elapsed;
            double milliseconds = timespan.TotalMilliseconds;  //  总毫秒数
            label2.Text = milliseconds.ToString() + " .REC";
#endif
            return 0;
        }
        void Nothinghandle()
        {

        }
        int namexist(string name)
        {
            for (int i = 0; i < userName.Count; i++)
                if (userName[i] == name) return i;
            return -1;
        }
        private int recongnize(FaceTemplate template)
        {
            bool match = false;

            int i;
            for (i = 0; i < UserTemplates.Count; i++)
            {
                foreach (FaceTemplate t in UserTemplates[i])
                {
                    float Siminarity = 0.0f;
                    FaceTemplate t1 = t;
                    FSDK.MatchFaces(ref template.templateData, ref t1.templateData, ref Siminarity);
                    float threashold = 0.0f;
                    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                    FSDK.GetMatchingThresholdAtFAR(0.25f, ref threashold);
                    //0.1表示程序误认为不同的人为检测对象的几率是0.1   该取值范围0~1
                    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                    if (Siminarity > threashold)
                    {
                        match = true;
                        break;
                    }
                }
                if (match)
                {
                    return i;
                }

            }
            return -1;
        }
        private void draw(FSDK.TFacePosition FacePosition)
        {
            if (FacePosition.w != 0)
            {
                gr.DrawRectangle(Pens.LightPink,
                                FacePosition.xc - FacePosition.w / 2,
                                FacePosition.yc - FacePosition.w / 2,
                                FacePosition.w,
                                FacePosition.w);//标示人脸

            }
        }
        private void clear()
        {
            userName.Clear();
            faceTemplates.Clear();
            UserTemplates.Clear();
            username = "";
            reset();
            recperson = -1;
        }

        
        void stop()
        {
            username = "";
            reset();
            NeedClose = true;
            FSDKCam.CloseVideoCamera(CameraHandle);
            FSDKCam.FinalizeCapturing();
        }

        //窗体响应
        private void button1_Click(object sender, EventArgs e)
        {
            username = textBox1.Text;
            btn1UserName = textBox1.Text;
            AddNewOne = true;
            btn1Click = true;
            btn1Count++;
            programState = ProgramState.psAddFace;

        }
        private void button2_Click(object sender, EventArgs e)
        {
            username = textBox2.Text;
            btn2UserName = textBox2.Text ;
            AddNewOne = true;
            btn2Click = true;
            btn2Count++;
            programState = ProgramState.psAddFace;
        }
        private void button3_Click(object sender, EventArgs e)
        {
            filestored();
        }
        private void pictureBox1_Click(object sender, EventArgs e)
        {

        }

        //文件系统
        int loadUsers()
        {
            try
            {
                StreamReader lsr = File.OpenText(".\\users.txt");
                int n = 2;
                int i;
                //label1.Text = "Find " + n.ToString() + " Users";
                for (i = 0; i < n; i++)
                {
                    string temp = lsr.ReadLine();
                    userName.Add(temp);
                    int m = Convert.ToInt16(lsr.ReadLine());
                    int j;
                    for (j = 0; j < m; j++)
                    {
                        string tfilepath = lsr.ReadLine();
                        FileStream stream = new FileInfo(tfilepath).OpenRead();
                        Byte[] buffer = new Byte[stream.Length];
                        //从流中读取字节块并将该数据写入给定缓冲区buffer中
                        stream.Read(buffer, 0, Convert.ToInt32(stream.Length));
                        FaceTemplate ftem;
                        ftem.templateData = buffer;
                        faceTemplates.Add(ftem);
                        stream.Close();
                    }
                    List<FaceTemplate> atemp = new List<FaceTemplate>(faceTemplates.ToArray());
                    UserTemplates.Add(atemp);
                    faceTemplates.Clear();
                }
                lsr.Close();
                Console.WriteLine("Load OK");
                Console.WriteLine(UserTemplates.Count.ToString());
                Console.WriteLine(userName.Count.ToString());
                return 0;
            }
            catch
            {
                Console.WriteLine("No user Stored");
                return 1;
            }
        }

        void filestored()
        {
            string path = ".\\Users";
            FileStream fs = new FileStream(".\\users.txt", FileMode.Create);
            fs.Seek(0, SeekOrigin.Begin);
            fs.SetLength(0);
            StreamWriter sw = new StreamWriter(fs);
            //sw.WriteLine(UserTemplates.Count.ToString());
            int i;
            for (i = 0; i < 2; i++)
            {
                int j;
                if (i == 0)
                {
                    sw.WriteLine(btn1UserName);
                    int usernum = (btn1Count+1) * REPEAT_REM;
                    sw.WriteLine(usernum);
                    for (j = 0; j < usernum; j++)
                    {
                        string upath = path + "\\" + btn1UserName + j.ToString() + ".dat";
                        sw.WriteLine(upath);
                    }
                }
                if (i == 1)
                {
                    sw.WriteLine(btn2UserName);
                    int usernum = (btn2Count+1) * REPEAT_REM;
                    sw.WriteLine(usernum);
                    for (j = 0; j < usernum; j++)
                    {
                        string upath = path + "\\" + btn2UserName + j.ToString() + ".dat";
                        sw.WriteLine(upath);
                    }
                }
            }
            sw.Close();
            fs.Close();
        }

        private void Form1_Closed(object sender, FormClosedEventArgs e)
        {
            //filestored();
            Environment.Exit(0);
        }

    }
}