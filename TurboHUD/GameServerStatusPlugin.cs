using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Net.Sockets;
using System.Diagnostics;
using System.Net.NetworkInformation;

/**
 * Shamalaya - Server Status Plugin v. 1.0.3
 * 
 * To rate the current server type the following command in chat when in town:
 * (N.B. if u write on group chat ensure there are min 2 players)
 * Tips: Join community on D3 -> "D3 Server Status" u can spam command there
 * 
 * cmd: \s rating    rating: 1 bad, 2, laggy, 3 good, 4 excellent Example: \s 3
 * 
 * bad (red)            => unplayable: server lags everywhere even in town or with few mobs  
 * laggy (yellow)       => lags but still playable: max 1 elite and few mobs around  
 * good (green)         => the server sometimes lags but you can definitely play on it: max 2 elites and mobs  
 * excellent (purple)   => no lag: 3 or more elites and mobs  
 * 
 * N.B.
 * -) When the server ip color is white meaning no rate found.
 * -) Star symbol means you rated the server
 * -) Cross symbol means the last rate is older than 3 days
 * -) Number betwen compounds indicates number of players that rated the server
 */
namespace Turbo.Plugins.Default
{
    public class GameServerStatusPlugin : BasePlugin, ICustomizer, IInGameTopPainter, IChatLineChangedHandler, INewAreaHandler
    {
        public const string VERSION = "1.0.3";

        private TopLabelDecorator GameClockDecorator { get; set; }

        // Server decorators
        private TopLabelDecorator ServerIpAddressDecoratorNoEntry { get; set; }
        private TopLabelDecorator ServerIpAddressDecoratorBad { get; set; }
        private TopLabelDecorator ServerIpAddressDecoratorLaggy { get; set; }
        private TopLabelDecorator ServerIpAddressDecoratorGood { get; set; }
        private TopLabelDecorator ServerIpAddressDecoratorExcellent { get; set; }

        // OnArea workaround
        private int indexMe = -1;

        // App server rating backend endpoint
        private MyClient client = new MyClient("35.159.16.254", 3000);

        // D3 current server info
        private string currentServerIP;
        private const int D3_PORT = 3724;

        // Current server rate
        private int rate = -1;
        private int nvotes = 0;
        private bool voted = false;
        private bool outdated = false;

        private const int FORCE_CHECK = 20;
        private Stopwatch lastServerCall = new Stopwatch();

        public GameServerStatusPlugin()
        {
            Enabled = true;
        }

        public void Customize()
        {
            // Disable Default GameInfoPlugin
            Hud.GetPlugin<GameInfoPlugin>().Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);

            // start last call server timer
            lastServerCall.Start();

            GameClockDecorator = new TopLabelDecorator(Hud)
            {
                TextFont = Hud.Render.CreateFont("tahoma", 7, 255, 255, 235, 170, false, false, true),
                TextFunc = () => new TimeSpan(Convert.ToInt64(Hud.Game.CurrentGameTick / 60.0f * 10000000)).ToString(@"hh\:mm\:ss"),
            };

            ServerIpAddressDecoratorNoEntry = new TopLabelDecorator(Hud)
            {
                TextFont = Hud.Render.CreateFont("tahoma", 6, 255, 255, 255, 255, false, false, true),
                TextFunc = () => showServerStatus(),
            };

            ServerIpAddressDecoratorBad = new TopLabelDecorator(Hud)
            {
                TextFont = Hud.Render.CreateFont("tahoma", 6, 255, 255, 0, 0, false, false, true),
                TextFunc = () => showServerStatus(),
            };

            ServerIpAddressDecoratorLaggy = new TopLabelDecorator(Hud)
            {
                TextFont = Hud.Render.CreateFont("tahoma", 6, 255, 255, 204, 0, false, false, true),
                TextFunc = () => showServerStatus(),
            };

            ServerIpAddressDecoratorGood = new TopLabelDecorator(Hud)
            {
                TextFont = Hud.Render.CreateFont("tahoma", 6, 255, 14, 255, 3, false, false, true),
                TextFunc = () => showServerStatus(),
            };

            ServerIpAddressDecoratorExcellent = new TopLabelDecorator(Hud)
            {
                TextFont = Hud.Render.CreateFont("tahoma", 6, 255, 255, 3, 232, false, false, true),
                TextFunc = () => showServerStatus(),
            };
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (Hud.Render.UiHidden)
                return;
            if (clipState != ClipState.BeforeClip)
                return;
            if ((Hud.Game.MapMode == MapMode.WaypointMap) || (Hud.Game.MapMode == MapMode.ActMap) || (Hud.Game.MapMode == MapMode.Map))
                return;

            var uiRect = Hud.Render.GetUiElement("Root.NormalLayer.minimap_dialog_backgroundScreen.minimap_dialog_pve.BoostWrapper.BoostsDifficultyStackPanel.clock").Rectangle;
            GameClockDecorator.Paint(uiRect.Left, uiRect.Top + (uiRect.Height * 1.15f), uiRect.Width, uiRect.Height * 0.7f, HorizontalAlign.Right);

            if (Hud.Game.IsInTown)
            {
                // if no server score or enough time elapsed
                if (rate == -1 || lastServerCall.Elapsed.TotalSeconds > FORCE_CHECK)
                {
                    // reset server info
                    currentServerIP = null;
                    rate = -1;
                    nvotes = 0;
                    voted = false;
                    outdated = false;

                    // retrive d3 server ip
                    var ip = IPGlobalProperties.GetIPGlobalProperties();
                    foreach (var tcp in ip.GetActiveTcpConnections())
                    {
                        if (tcp.RemoteEndPoint.Port == D3_PORT)
                        {
                            currentServerIP = tcp.RemoteEndPoint.Address.MapToIPv4().ToString();
                            break;
                        }
                    }

                    // check if server ip is valid
                    if (currentServerIP == null) return;

                    // start call server timer
                    lastServerCall.Restart();

                    try
                    {
                        ServerRating sr = new ServerRating()
                        {
                            cmd = "GET",
                            serverIP = currentServerIP,
                            battletag = FastHash(Hud.MyBattleTag)
                        };

                        var ris = client.callServer(sr);
                        if ("OK".Equals(ris["result"].ToString()))
                        {
                            rate = Int32.Parse(ris["rating"].ToString());
                            nvotes = Int32.Parse(ris["nvotes"].ToString());
                            voted = Boolean.Parse(ris["voted"].ToString());
                            outdated = Boolean.Parse(ris["outdated"].ToString());
                            Hud.TextLog.Log("GameServerStatusPlugin", $"Server IP: {currentServerIP} - score: {rate} - nvotes: {nvotes} - voted: {voted} - outdated: {outdated}");
                        }
                        else
                        {
                            Hud.TextLog.Log("GameServerStatusPlugin", $"Error: {ris["error"]}");
                        }
                    }
                    catch (Exception e)
                    {
                        Hud.TextLog.Log("GameServerStatusPlugin", "Error: " + (e != null ? e.Message : "null"));
                    }
                }

                // display server score
                switch (rate)
                {
                    case 0:
                        ServerIpAddressDecoratorNoEntry.Paint(uiRect.Left, uiRect.Top + (uiRect.Height * 1.85f), uiRect.Width, uiRect.Height * 0.7f, HorizontalAlign.Right);
                        break;
                    case 1:
                        ServerIpAddressDecoratorBad.Paint(uiRect.Left, uiRect.Top + (uiRect.Height * 1.85f), uiRect.Width, uiRect.Height * 0.7f, HorizontalAlign.Right);
                        break;
                    case 2:
                        ServerIpAddressDecoratorLaggy.Paint(uiRect.Left, uiRect.Top + (uiRect.Height * 1.85f), uiRect.Width, uiRect.Height * 0.7f, HorizontalAlign.Right);
                        break;
                    case 3:
                        ServerIpAddressDecoratorGood.Paint(uiRect.Left, uiRect.Top + (uiRect.Height * 1.85f), uiRect.Width, uiRect.Height * 0.7f, HorizontalAlign.Right);
                        break;
                    case 4:
                        ServerIpAddressDecoratorExcellent.Paint(uiRect.Left, uiRect.Top + (uiRect.Height * 1.85f), uiRect.Width, uiRect.Height * 0.7f, HorizontalAlign.Right);
                        break;
                }
            }
        }

        public void OnChatLineChanged(string currentLine, string previousLine)
        {
            try
            {
                if (!Hud.Game.Me.IsInTown) return;
                if (currentServerIP == null) return;
                if (currentLine == null) return;

                // new msg
                if (!currentLine.Equals(previousLine))
                {
                    // Hud.TextLog.Log("GameServerStatusPlugin", "CL: " + currentLine);
                    Match m = Regex.Match(currentLine, @"(?:h\[(.*)\])\|(?:h.*\\s\s([1234])$)");

                    if (m.Success)
                    {
                        string user = m.Groups[1].Value;
                        string newRate = m.Groups[2].Value;

                        string tagname = user;
                        // strip clan name
                        if (user.Contains(" "))
                            tagname = user.Split(' ')[1];

                        // check tagname
                        if (String.IsNullOrWhiteSpace(tagname)) return;
                        if (!tagname.Equals(Hud.MyBattleTag.Split('#')[0])) return;

                        // post new rate
                        ServerRating sr = new ServerRating()
                        {
                            cmd = "POST",
                            serverIP = currentServerIP,
                            battletag = FastHash(Hud.MyBattleTag),
                            rating = Int32.Parse(newRate)
                        };

                        try
                        {
                            var ris = client.callServer(sr);
                            if ("OK".Equals(ris["result"].ToString()))
                            {
                                Hud.TextLog.Log("GameServerStatusPlugin", $"New Rating IP: {currentServerIP} - BTag: {Hud.MyBattleTag} - Rate: {newRate}");
                                rate = -1;
                            }
                            else
                                Hud.TextLog.Log("GameServerStatusPlugin", $"Error: {ris["error"]}");
                        }
                        catch (Exception e)
                        {
                            Hud.TextLog.Log("GameServerStatusPlugin", "Error: " + (e != null ? e.Message : "null"));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Hud.TextLog.Log("GameServerStatusPlugin", "Error: " + (e != null ? e.Message : "null"));
            }
        }

        public void OnNewArea(bool newGame, ISnoArea area)
        {
            if (newGame || indexMe != Hud.Game.Me.Index)
            {
                // Hud.TextLog.Log("GameServerStatusPlugin", "New game started ...");
                indexMe = Hud.Game.Me.Index;

                // reset server score
                currentServerIP = null;
                rate = -1;
                nvotes = 0;
                voted = false;
                outdated = false;
            }
        }

        private class MyClient
        {
            private string ip { get; set; }
            private int port { get; set; }

            public MyClient(string ip, int port)
            {
                this.ip = ip;
                this.port = port;
            }

            public JObject callServer(ServerRating sr)
            {
                TcpClient client = null;
                NetworkStream stream = null;

                try
                {
                    // prepare data
                    string jsonString = JsonConvert.SerializeObject(sr);
                    byte[] data = Encoding.UTF8.GetBytes(jsonString);

                    // connetti 
                    client = new TcpClient(this.ip, this.port);
                    stream = client.GetStream();

                    // Invia la stringa JSON al server
                    stream.Write(data, 0, data.Length);

                    // Legge la risposta del server
                    byte[] buffer = new byte[128];
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    string responseString = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    // Deserializza la risposta JSON
                    JObject response = (JObject)JsonConvert.DeserializeObject(responseString);
                    return response;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    return null;
                }
                finally
                {
                    if (stream != null) stream.Close();
                    if (client != null) client.Close();
                }
            }
        }

        private class ServerRating
        {
            public string version = VERSION;
            public string cmd { get; set; }
            public string serverIP { get; set; }
            public string battletag { get; set; }
            public int rating { get; set; }
        }

        private static string FastHash(string input)
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            uint hash = 2166136261;

            for (int i = 0; i < inputBytes.Length; i++)
            {
                hash = (hash * 16777619) ^ inputBytes[i];
            }

            return hash.ToString("X8");
        }

        private string showServerStatus()
        {
            StringBuilder sb = new StringBuilder();

            if (voted)
            {
                sb.Append($"{Char.ConvertFromUtf32(0x2606)} ");
            }

            if (outdated)
            {
                sb.Append($"{Char.ConvertFromUtf32(0x1F547)} ");
            }

            sb.Append($"({nvotes}) {currentServerIP}");
            return sb.ToString();
        }
    }
}