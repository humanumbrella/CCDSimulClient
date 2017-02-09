using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using CCDSimulServer;
using System.Diagnostics;
using System.IO;
namespace CCDSimulClient
{

    public partial class ClientForm : Form
    {
        
        private Socket _clientSocket;
        private int port = 0;
        private byte[] _buffer;
        FolderBrowserDialog fbd = new FolderBrowserDialog();
        private string maximpath = @"C:\Program Files (x86)\Diffraction Limited\MaxIm DL V5\MaxIm_DL.exe";
        //for p8 uncomment
        //private string maximpath = @"C:\Program Files\Diffraction Limited\MaxIm DL V5\MaxIm_DL.exe";
        
        private string savepath = @"C:\Users\jmoore\Dropbox\Polarimetry\";
        //for p8 uncomment
        //private string savepath = @"C:\Skynet-Testing\";

        private ushort epsilon = 0;
        private double expTime = 0;
        private bool isDark;
        private bool saveImages;
        private bool clientSetSavePath = false;

        private string filter = string.Empty;
        private int orderPos = -1;
        

        private MaxIm.CCDCamera ccd;

        public ClientForm()
        {
            InitializeComponent();
            //if maxim isn't open - let's open it.
            Process[] localByName = Process.GetProcessesByName("MaxIm_DL");
            /*
            if (localByName.Length == 0)
            {

                Process.Start(maximpath);
            }*/

            while (true)
            {
                try
                {
                    if (localByName.Length == 0)
                    {
                        Process.Start(maximpath);
                        break;
                    }
                    break;

                }
                catch (Exception error)
                {
                    Console.WriteLine(error.ToString());
                    MessageBox.Show("Choose folder where MaximDL lives...");
                    DialogResult result = fbd.ShowDialog();
                    if (!string.IsNullOrWhiteSpace(fbd.SelectedPath))
                    {
                        maximpath = fbd.SelectedPath + "\\MaxIm_DL.exe";
                    }
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (ccd.LinkEnabled == true)
            {
                ccd.CoolerOn = false;
                ccd.LinkEnabled = false;
            }


            if (_clientSocket != null && !_clientSocket.IsBound)
            {
                _clientSocket.Shutdown(SocketShutdown.Both);
                _clientSocket.Close();
            }
        }

        private void btnCon_Click(object sender, EventArgs e)
        {
            _clientSocket = new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp);
            Int32.TryParse(tbPort.Text, out port);
            _clientSocket.BeginConnect(new IPEndPoint(IPAddress.Parse(tbIp.Text), port), new AsyncCallback(connectCallback), null);
            toggleConnect(true);
        }

        private void connectCallback(IAsyncResult ar)
        {
            try
            {
                _clientSocket.EndConnect(ar);
                AppendToTextBox("Connected to server!");
                btnSend.Enabled = true;
                _buffer = new byte[_clientSocket.ReceiveBufferSize];
                _clientSocket.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.None, new AsyncCallback(receiveCallback), null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);

            }
        }

        private void receiveCallback(IAsyncResult ar)
        {
            try
            {
                int received = _clientSocket.EndReceive(ar);

                if (received == 0)
                {
                    IPEndPoint clientIP = (IPEndPoint)_clientSocket.LocalEndPoint;
                    AppendToTextBox("Server " + clientIP.Address.ToString() + " disconnected!");
                    return; //eliminate the beginreceive below... bc client likely disconnected
                }
                Array.Resize(ref _buffer, received);

                string cmd = Encoding.ASCII.GetString(_buffer);
                if (cmd.Equals("setup_ccd"))
                {

                    ccd = new MaxIm.CCDCamera();

                    ccd.DisableAutoShutdown = true;
                    ccd.LinkEnabled = true;
                    ccd.CoolerOn = true;
                    AppendToTextBox("Client Camera Is Ready!");
                    sendToServer("Client Camera Is Ready!");
                }
                else if (cmd.Equals("teardown_ccd"))
                {
                    try
                    {
                        ccd.CoolerOn = false;
                        ccd.LinkEnabled = false;
                        
                        AppendToTextBox("Client Camera Is Disconnected!");
                        sendToServer("Client Camera Is Disconnected!");
                    }
                    catch (Exception ex)
                    {
                        AppendToTextBox("Cant Connect To Client Camera");
                        sendToServer("Cant Connect To Client Camera");
                        MessageBox.Show(ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);

                    }
                }
                else { 
                    ExposePackage recvd = new ExposePackage(_buffer);

                    epsilon = recvd.epsilon;
                    expTime = recvd.expTime;
                    isDark = recvd.isDark;
                    saveImages = recvd.saveImages;
                    if (!clientSetSavePath)
                    {
                        savepath = recvd.savepath;

                    }
                    filter = recvd.filter;
                    orderPos = recvd.orderPos;

                    waitFor(epsilon);
                    exposeFor(isDark, expTime);

                    while (!ccd.ImageReady)
                    {
                        //wait
                    }

                    string dt = string.Empty;
                    double jd = 0.0;
                    dt = ccd.Document.GetFITSKey("DATE-OBS");
                    AppendToTextBox("client_cam: " + dt);
                    sendToServer(" client_cam: " + dt);
                    jd = ccd.Document.GetFITSKey("JD");
                    AppendToTextBox("client_cam: " + jd.ToString());
                    sendToServer(" client_cam: " + jd.ToString());
                }
                /*
                string text = Encoding.ASCII.GetString(_buffer);
                //handle the packet
                AppendToTextBox(text);
                handlePacket(text);*/
                Array.Resize(ref _buffer, _clientSocket.ReceiveBufferSize);
                 
                //restart receive
                _clientSocket.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.None, new AsyncCallback(receiveCallback), null);

            }
            catch (Exception ex)
            {
                AppendToTextBox("Server Disconnected!");
                if (!ex.Message.Contains("Cannot access a disposed object"))
                {
                    MessageBox.Show(ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

            }
        }

        private void handlePacket(string text)
        {
            switch (text)
            {
                case "setup_ccd":
                    try
                    {
                        ccd = new MaxIm.CCDCamera();
                        ccd.LinkEnabled = true;
                        AppendToTextBox("Client Camera Is Ready!");
                        sendToServer("Client Camera Is Ready!");
                    }
                    catch (Exception ex)
                    {
                        AppendToTextBox("Cant Connect To Client Camera");
                        sendToServer("Cant Connect To Client Camera");
                        MessageBox.Show(ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);

                    }
                    break;

                case "teardown_ccd":
                    try
                    {
                        ccd.LinkEnabled = false;
                        AppendToTextBox("Client Camera Is Disconnected!");
                        sendToServer("Client Camera Is Disconnected!");
                    }
                    catch (Exception ex)
                    {
                        AppendToTextBox("Cant Connect To Client Camera");
                        sendToServer("Cant Connect To Client Camera");
                        MessageBox.Show(ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);

                    }
                    break;
                case "expose_ccd":
                    ccd.Expose(1, 1, 0);

                    while (!ccd.ImageReady)
                    {
                        //wait
                    }
                    AppendToTextBox("Client Exposure Is Done!");
                    sendToServer("Client Exposure Is Done!");
                    break;
                default:
                    break;

            }
        
        
        }

        private void waitFor(ushort s)
        {
            Thread.Sleep(s*1000);
        }
        private void exposeFor(bool dark, double exp)
        {

            ccd.Expose((double)exp, Convert.ToInt16(!dark));

            while (!ccd.ImageReady)
            {
                //wait
            }
            sendToServer("Client Camera Exposure Is Done!");

            string dt = string.Empty;
            dt = ccd.Document.GetFITSKey("DATE-OBS");
            checkForDirectory(savepath);
            string testing = string.Empty;
            if (saveImages)
            {
                if (orderPos > -1)
                {
                    testing = savepath + dt.Replace(":", "-") +"-"+ filter + "-" + orderPos + "-U47.fit";

                }
                else
                {
                    testing = savepath + dt.Replace(":", "-") + "-U47.fit";
                }
                ccd.SaveImage(testing);
                sendToServer("Client Camera Exposure Saved!");
                sendToServer("Client:" + savepath);
                AppendToTextBox("Client Camera Exposure Saved!");
                AppendToTextBox("Client:" + savepath);
            }
            else
            {
                sendToServer("Client Camera Exposure *not* saved!");
                AppendToTextBox("Client Camera Exposure *not* saved!");
            }
        }
        private void checkForDirectory(string fp)
        {
            if (!Directory.Exists(fp))
            {
                Directory.CreateDirectory(fp);
            }
        }
        private void AppendToTextBox(string text)
        {
            MethodInvoker invoker = new MethodInvoker(delegate
            {
                tbLog.AppendText(text + "\r\n");
                if (text.Contains("disconnected"))
                {
                    toggleConnect(false);
                    if (ccd != null)
                    {
                        ccd.CoolerOn = false;
                        ccd.LinkEnabled = false;
                    }
                }
            });
            this.Invoke(invoker);
        }

        private void clearSendTb()
        {

            MethodInvoker invoker = new MethodInvoker(delegate
            {
                tbSend.Text = "";
            });
            this.Invoke(invoker);
        }
        private void sendToServer(string toSend)
        {
            try
            {
                byte[] buffer = Encoding.ASCII.GetBytes(toSend+"\r\n");
                _clientSocket.BeginSend(buffer, 0, buffer.Length, SocketFlags.None, new AsyncCallback(SendCallback), null);
                clearSendTb();

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);

            }
        }
        private void btnSend_Click(object sender, EventArgs e)
        {
            try
            {
                byte[] buffer = Encoding.ASCII.GetBytes(tbSend.Text);
                _clientSocket.BeginSend(buffer, 0, buffer.Length, SocketFlags.None, new AsyncCallback(SendCallback), null);
                clearSendTb();

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);

            }
        }

        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                _clientSocket.EndSend(ar);
            }

            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);

            }
        }

        private void toggleConnect(bool change)
        {
            if (change)
            {
                btnDiscon.Enabled = true;
                btnCon.Enabled = false;
            }
            else
            {
                btnDiscon.Enabled = false;
                btnCon.Enabled = true;
            }
        }

        private void btnDiscon_Click(object sender, EventArgs e)
        {
            _clientSocket.Shutdown(SocketShutdown.Both);
            _clientSocket.Close();
            //bug on close
            //_clientSocket = null;
            toggleConnect(false);
        }


        private void setSavePathToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DialogResult result = fbd.ShowDialog();
            if (!string.IsNullOrWhiteSpace(fbd.SelectedPath))
            {
                savepath = fbd.SelectedPath + "\\";
            }
            clientSetSavePath = true;
            btnGCPFS.Enabled = true;


            AppendToTextBox("Client Path Updated:");
            AppendToTextBox(savepath + "\n");
            sendToServer("Client set custom save directory!  Your sends will be ignored unless they uncheck.");
        }

        private void btnGCPFS_Click(object sender, EventArgs e)
        {
            clientSetSavePath = false;
            btnGCPFS.Enabled = false;
        }

    }
}
