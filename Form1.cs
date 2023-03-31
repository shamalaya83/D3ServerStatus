﻿using System;
using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace D3ServerStatus
{
    public partial class Form1 : Form
    {
        // device id
        private static string deviceId = FastHash.CalculateUUID();

        // D3 server infos
        private string connectedServerIP = null;
        private int rating = -1;

        // server rating client
        private static MyClient client = new MyClient("35.159.16.254", 3000);
        private const int D3_PORT = 3724;

        // timer
        private static Timer myTimer = new Timer();
        private int counter = 0;
        private const int FORCE_UPDATE = 20;

        // mutex
        private static Mutex mut = new Mutex();

        public Form1()
        {
            InitializeComponent();

            // reset
            ResetServerStatus();
            SetUiDisconnected();

            // initialize serverloop background worker
            this.serverLoopWorker.DoWork += this.ServerLoopWorker_DoWork;
            this.serverLoopWorker.RunWorkerCompleted += this.ServerLoopWorker_RunWorkerCompleted;

            // initialize servervote background worker
            this.serverVoteWorker.DoWork += this.ServerVoteWorker_DoWork;
            this.serverVoteWorker.RunWorkerCompleted += this.ServerVoteWorker_RunWorkerCompleted;

            // Sets the timer interval to 5 seconds.            
            myTimer.Tick += new EventHandler(TimerEventProcessor);
            myTimer.Interval = 1000;
            myTimer.Start();
        }

        /**
         * Timer
         */
        #region Timer
        private void TimerEventProcessor(Object myObject, EventArgs myEventArgs)
        {
            // stop timer
            myTimer.Stop();
            // inc counter
            counter++;
            // check server status
            this.serverLoopWorker.RunWorkerAsync();
        }
        #endregion

        /**
         * Server Staus Worker
         */
        #region Server Staus Worker
        private void ServerLoopWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                // There was an error during the operation.                
                var response = MessageBox.Show($"An error occurred: {e.Error.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (response == DialogResult.OK)
                {
                    Application.Exit();
                }
            }
            else
            {
                // The operation completed normally.
                setUi();

                // restart the timer
                myTimer.Enabled = true;
            }
        }

        private void ServerLoopWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            // Do not access the form's BackgroundWorker reference directly.
            // Instead, use the reference provided by the sender parameter.
            BackgroundWorker bw = sender as BackgroundWorker;

            try
            {
                mut.WaitOne();

                // Start the time-consuming operation.
                CheckServer(bw);
            }
            finally
            {
                mut.ReleaseMutex();
            }
        }

        private void CheckServer(BackgroundWorker bw)
        {            
            // reset current server IP
            string currentServerIP = null;

            // find D3 current connected server
            var ip = IPGlobalProperties.GetIPGlobalProperties();
            foreach (var tcp in ip.GetActiveTcpConnections())
            {
                if (tcp.RemoteEndPoint.Port == D3_PORT)
                {
                    currentServerIP = tcp.RemoteEndPoint.Address.MapToIPv4().ToString();
                    break;
                }
            }

            // check disconnected
            if (currentServerIP == null)
            {
                // reset server info
                connectedServerIP = currentServerIP;
                rating = -1;

                // reset counter
                counter = 0;

                return;
            }            

            // get server rating on new server, after voting or after a while
            if (!currentServerIP.Equals(connectedServerIP) || rating == -1 || counter > FORCE_UPDATE)
            {
                // reset counter
                counter = 0;

                // call server rating
                ServerRating sr = new ServerRating()
                {
                    cmd = "GET",
                    serverIP = currentServerIP
                };

                var ris = client.callServer(sr);
                if (ris == null)
                {
                    throw new Exception("Rate Server Unreachable");
                }

                if ("OK".Equals(ris["result"].ToString()))
                {
                    // save server info
                    connectedServerIP = currentServerIP;
                    rating = Int32.Parse(ris["rating"].ToString());
                    return;
                }
                else
                {
                    throw new Exception($"Error: {ris["error"]}");
                }
            }
        }
        #endregion

        /**
         * Server Vote Worker
         */
        #region Server Staus Worker
        private void ServerVoteWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                // There was an error during the operation.                
                var response = MessageBox.Show($"An error occurred: {e.Error.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (response == DialogResult.OK)
                {
                    Application.Exit();
                }
            }
            else
            {
                // The operation completed normally.
                this.buttonVote.Enabled = true;
            }
        }

        private void ServerVoteWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            // Do not access the form's BackgroundWorker reference directly.
            // Instead, use the reference provided by the sender parameter.
            BackgroundWorker bw = sender as BackgroundWorker;

            try
            {
                mut.WaitOne();

                // Start the time-consuming operation.
                VoteServer(bw);
            }
            finally
            {
                mut.ReleaseMutex();
            }
        }

        private void VoteServer(BackgroundWorker bw)
        {
            // check server ip & rating
            if (connectedServerIP != null && GetRate() > 0)
            {
                // call server rating
                ServerRating sr = new ServerRating()
                {
                    cmd = "POST",
                    serverIP = connectedServerIP,
                    battletag = deviceId,
                    rating = GetRate()
                };

                var ris = client.callServer(sr);
                if (ris == null)
                {
                    throw new Exception("Rate Server Unreachable");
                }

                if ("OK".Equals(ris["result"].ToString()))
                {
                    Console.WriteLine($"Server IP: {connectedServerIP} rated with score: {sr.rating}");
                    rating = -1;
                    return;
                }
                else
                {
                    throw new Exception($"Error: {ris["error"]}");
                }
            }
        }
        #endregion

        /**
         * Status & UI
         */
        #region Status & Ui
        private void ResetServerStatus()
        {
            this.connectedServerIP = null;
            this.rating = -1;
        }

        private void SetUiDisconnected()
        {
            this.currentserverip.Text = "Disconnected";
            this.serverrating.Text = "N.A.";
            this.serverrating.ForeColor = System.Drawing.Color.Black;
            this.rateButtonBad.Checked = false;
            this.rateButtonLag.Checked = false;
            this.rateButtonGood.Checked = false;
            this.rateButtonExcellent.Checked = false;
            this.buttonVote.Enabled = false;
            this.groupBoxRate.Enabled = false;
        }

        private void setUi()
        {
            switch (rating)
            {
                case 0:
                    {
                        this.currentserverip.Text = connectedServerIP;
                        this.serverrating.Text = "not reviewed";
                        this.serverrating.ForeColor = System.Drawing.Color.DarkGray;
                        this.groupBoxRate.Enabled = true;
                        break;
                    }
                case 1:
                    {
                        this.currentserverip.Text = connectedServerIP;
                        this.serverrating.Text = "bad";
                        this.serverrating.ForeColor = System.Drawing.Color.Red;
                        this.groupBoxRate.Enabled = true;
                        break;
                    }
                case 2:
                    {
                        this.currentserverip.Text = connectedServerIP;
                        this.serverrating.Text = "laggy";
                        this.serverrating.ForeColor = System.Drawing.Color.DarkOrange;
                        this.groupBoxRate.Enabled = true;
                        break;
                    }
                case 3:
                    {
                        this.currentserverip.Text = connectedServerIP;
                        this.serverrating.Text = "good";
                        this.serverrating.ForeColor = System.Drawing.Color.Green;
                        this.groupBoxRate.Enabled = true;
                        break;
                    }
                case 4:
                    {
                        this.currentserverip.Text = connectedServerIP;
                        this.serverrating.Text = "excellent";
                        this.serverrating.ForeColor = System.Drawing.Color.Purple;
                        this.groupBoxRate.Enabled = true;
                        break;
                    }
                default:
                    {
                        SetUiDisconnected();
                        break;
                    }
            }
        }

        private int GetRate()
        {
            if (this.rateButtonBad.Checked) return 1;
            if (this.rateButtonLag.Checked) return 2;
            if (this.rateButtonGood.Checked) return 3;
            if (this.rateButtonExcellent.Checked) return 4;
            return 0;
        }

        private void rateButtonBad_CheckedChanged(object sender, EventArgs e)
        {
            this.buttonVote.Enabled = true;
        }

        private void rateButtonLag_CheckedChanged(object sender, EventArgs e)
        {
            this.buttonVote.Enabled = true;
        }

        private void rateButtonGood_CheckedChanged(object sender, EventArgs e)
        {
            this.buttonVote.Enabled = true;
        }

        private void rateButtonExcellent_CheckedChanged(object sender, EventArgs e)
        {
            this.buttonVote.Enabled = true;
        }

        private void buttonVote_Click(object sender, EventArgs e)
        {
            this.buttonVote.Enabled = false;
            this.serverVoteWorker.RunWorkerAsync();
        }
        #endregion
    }
}