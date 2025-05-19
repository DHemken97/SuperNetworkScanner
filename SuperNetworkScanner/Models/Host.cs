namespace SuperNetworkScanner.Models
{
    public class Host
    {
        public string Hostname { get; set; }
        public List<NetworkInterface> NetworkInterfaces { get; set; }

    }
}
