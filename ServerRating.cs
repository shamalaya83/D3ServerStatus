namespace D3ServerStatus
{
    public class ServerRating
    {
        public string version = Form1.VERSION;
        public string cmd { get; set; }
        public string serverIP { get; set; }
        public string battletag { get; set; }
        public int rating { get; set; }
    }
}
