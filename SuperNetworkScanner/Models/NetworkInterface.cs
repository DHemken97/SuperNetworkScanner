namespace SuperNetworkScanner.Models
{
    public class NetworkInterface
    {
        public string Name { get; set; }
        public string MAC { get; set; }
        public List<string> Ip_Address { get; set; } = new List<string>();
        public List<Service> Services { get; set; } = new List<Service>();

    }
}
