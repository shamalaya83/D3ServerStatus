using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Sockets;
using System.Text;

namespace D3ServerStatus
{
    public class MyClient
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
}
